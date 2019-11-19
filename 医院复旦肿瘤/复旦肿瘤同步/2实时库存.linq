<Query Kind="Program">
  <Reference>D:\LINQPad\packages\HealthCare.Data.MongoModel.dll</Reference>
  <Reference>D:\LINQPad\packages\log4net.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Bson.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Driver.Core.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Driver.dll</Reference>
  <Reference>D:\LINQPad\packages\Newtonsoft.Json.dll</Reference>
  <Reference>D:\LINQPad\packages\Oracle.ManagedDataAccess.dll</Reference>
  <Reference>D:\LINQPad\packages\Oracle.ManagedDataAccess.EntityFramework.dll</Reference>
  <Reference>D:\LINQPad\packages\System.Net.Http.Formatting.dll</Reference>
  <Namespace>HealthCare.Data</Namespace>
  <Namespace>log4net</Namespace>
  <Namespace>log4net.Config</Namespace>
  <Namespace>MongoDB.Bson</Namespace>
  <Namespace>MongoDB.Driver</Namespace>
  <Namespace>MongoDBContext = HealthCare.Data.MongoContext</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>Newtonsoft.Json.Linq</Namespace>
  <Namespace>Oracle.ManagedDataAccess.Client</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>Newtonsoft.Json.Serialization</Namespace>
</Query>

private static MongoDBContext mongo = new MongoContext("mongodb://127.0.0.1:27017", "FDZL");
public ICustomerRepository customerRepository = new CustomerRepository(mongo);
public ITerminalGoodsRepository terminalGoodsRepository = new TerminalGoodsRepository(mongo);

void Main()
{
	var setting = new Newtonsoft.Json.JsonSerializerSettings();
	JsonConvert.DefaultSettings = new Func<JsonSerializerSettings>(() =>
	{
		setting.ContractResolver = new CamelCasePropertyNamesContractResolver();
		return setting;
	});
	RealtimeInventory("fdzl_realtime_inventory");
}

/// <summary>
///     获取设备的实时库存
/// </summary>
/// <param name="appcode"></param>
/// <param name="nodes">节点编码。查询多个设备时按照 ; 进行分隔，查询所有设备时传递 null 或 ""</param>
/// <returns></returns>
public void RealtimeInventory(string appcode, string nodes = null)
{
	var dpts = nodes?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
	var devices = customerRepository.NodeDevices(dpts);
	var computers = devices.Select(c => c.node.computer).Distinct().ToArray();
	var tGoods = computers.Any() ? terminalGoodsRepository.TerminalGoods(computers) : new HealthCare.Data.TerminalGoods[0];

	var gIds = devices.SelectMany(d => d.devices).SelectMany(f => f.Drawers).SelectMany(f => f.Boxes).SelectMany(f => f.Fills).Select(f => f.GoodsId).Distinct()
		.Except(tGoods.Select(t => t.GoodsId).Distinct()).ToArray();
	var gs = gIds.Any() ? mongo.GoodsCollection.AsQueryable().Where(o => gIds.Contains(o.UniqueId)).ToArray() : new Goods[0];

	var invs = devices.SelectMany(d =>
	{
		var fills = d.devices.SelectMany(f => f.Drawers).SelectMany(f => f.Boxes).SelectMany(f => f.Fills).ToArray();
		return fills.GroupBy(o => new { o.GoodsId, o.BatchNumber, o.ExpiredDate, }).Select(o =>
		{
			var tgs = tGoods.Where(f => f.Computer == d.node.computer && f.GoodsId == o.Key.GoodsId);
			return new
			{
				Customer = d.node.customer,
				Computer = d.node.computer,
				DepartmentId = d.node.department,
				DepartmentName = d.devices.Select(f => f.Department?.DisplayName).FirstOrDefault(f => !string.IsNullOrEmpty(f)),
				d.devices.Select(f => (f.No, f.DisplayText)).FirstOrDefault().No,
				d.devices.Select(f => (f.No, f.DisplayText)).FirstOrDefault().DisplayText,
				o.Key.GoodsId,
				Goods = tgs.Select(f => f.Goods).FirstOrDefault() ?? gs.FirstOrDefault(x => x.UniqueId == o.Key.GoodsId),
				o.Key.BatchNumber,
				o.Key.ExpiredDate,
				StorageQuota = tgs.Sum(f => f.StorageQuota),
				WarningQuota = tgs.Sum(f => f.WarningQuota),
				CurrentQuota = o.Sum(f => f.QtyExisted),
			};
		});
	}).ToArray();

	var values = invs.Select(o => JObject.Parse(JsonConvert.SerializeObject(o))).ToArray();
	var data = __filter_inventories__(values);

	dynamic result = fdzl_realtime_inventory(data, mongo);
	(result as object[]).Dump();
	// return Json(result);
}

public static JObject[] __filter_inventories__(JObject[] data)
{
	var vals = data.Where(d =>
	{
		var b = d["batchNumber"]?.ToObject<string>() ?? string.Empty;
		var e = d["expiredDate"]?.ToObject<DateTime>() ?? DateTime.MaxValue;
		var q = d["currentQuota"]?.ToObject<double>() ?? 0.0;
		// 过滤只进行了占位的物品（无批号且无有效期且无库存）
		return !(string.IsNullOrEmpty(b) && e.Date == DateTime.MaxValue.Date && q == 0.0);
	}).ToArray();
	return vals;
}

/// <summary>
///     上海复旦肿瘤医院，实时库存接口
/// </summary>
public static dynamic fdzl_realtime_inventory(JObject[] data, MongoDBContext mongo)
{
	//字段名	字段含义	字段长度
	//drugId	药品ID	String
	//drugName	名称	String
	//specification	规格	String
	//mafcName	厂家名称	String
	//batchNo	生产批号	String
	//productDate	生产日期	Date
	//validDate	有效期	Date
	//currentQuantity	库存量	Integer
	//quantityUnit	单位	String
	//wardCode	病区代码	String
	//wardName	病区名称	String
	//equipCode	设备代码	String
	//equipName	设备名称	String
	//remark	备注	String

	var computers = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).SelectMany(c => c.Cabinets)
		.Where(c => c.IsPrimary && !c.DisplayText.Contains("抢救车"))
		.Select(c => c.Computer).Distinct().ToArray();
	data = data.Where(d => computers.Contains(d["computer"].ToObject<string>())).ToArray();

	var xs = data.Select(d =>
	{
		var validDate = GetValueByPath<DateTime?>(d, "expiredDate") ?? DateTime.MaxValue.Date;
		var offset = Math.Abs((validDate - validDate.Date).TotalSeconds);
		return new
		{
			drugId = GetValueByPath<string>(d, "goodsId"),
			drugName = GetValueByPath<string>(d, "goods.displayName"),
			specification = GetValueByPath<string>(d, "goods.specification"),
			mafcName = GetValueByPath<string>(d, "goods.manufacturer"),
			batchNo = GetValueByPath<string>(d, "batchNumber"),
			productDate = dateStr(validDate.AddDays(-offset)),
			validDate = dateStr(validDate.Date),
			currentQuantity = GetValueByPath<int>(d, "currentQuota"),
			quantityUnit = GetValueByPath<string>(d, "goods.usedUnit"),
			wardCode = GetValueByPath<string>(d, "departmentId"),
			wardName = GetValueByPath<string>(d, "departmentName"),
			equipCode = GetValueByPath<string>(d, "computer"),
			equipName = GetValueByPath<string>(d, "displayText"),
			remark = "实时库存",
		};
	}).ToArray();
	return xs;

	T GetValueByPath<T>(JObject obj, string path)
	{
		JToken prop = obj;
		foreach (var chunk in path?.Split('.') ?? new string[0])
		{
			if (prop == null)
			{
				break;
			}
			if (prop is JObject pv)
			{
				prop = pv.GetValue(chunk, StringComparison.OrdinalIgnoreCase);
			}
			else
			{
				switch (prop.Type)
				{
					case JTokenType.None:
					case JTokenType.Null:
					case JTokenType.Undefined: prop = null; break;
					default: prop = prop.Value<JToken>(chunk); break;
				}
			}
		}
		var value = prop == null ? default : prop.Value<T>();
		return value;
	}

	string dateStr(DateTime dt) => dt.Date == DateTime.MaxValue.Date ? "" : dt.ToString("yyyy-MM-dd");
}


public interface ICustomerRepository
{
	CabinetDevice[] Devices(string[] departments);
	((string customer, string department, string computer) node, CabinetDevice[] devices)[] NodeDevices(string[] departments);
}

public class CustomerRepository : ICustomerRepository
{
	private readonly MongoDBContext mongo;

	public CustomerRepository(MongoDBContext mongo)
	{
		this.mongo = mongo;
	}

	public CabinetDevice[] Devices(string[] departments)
	{
		var cs = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).Select(o => new { o.Cabinets, o.OutOfCabinets, }).ToArray();
		var cabinets = cs.SelectMany(o => o.Cabinets).Concat(cs.SelectMany(o => o.OutOfCabinets)).Where(x => departments.Length <= 0 || departments.Contains(x.DepartmentId)).ToArray();
		foreach (var item in cabinets.SelectMany(c => c.Drawers).SelectMany(d => d.Boxes).SelectMany(b => b.Fills))
		{
			item.BatchNumber = item.BatchNumber ?? string.Empty;
		}
		return cabinets;
	}

	public ((string customer, string department, string computer) node, CabinetDevice[] devices)[] NodeDevices(string[] departments)
	{
		var cabinets = Devices(departments);
		var nds = cabinets.GroupBy(o => new { o.OwnerCode, o.DepartmentId }).SelectMany(gp =>
		{
			return gp.GroupBy(o => o.Computer).Select(gp1 =>
			{
				var node = (customer: gp.Key.OwnerCode, department: gp.Key.DepartmentId, computer: gp1.Key);
				return (node, devices: gp1.OrderBy(o => o.No).ToArray());
			});
		}).ToArray();
		return nds;
	}
}

public interface ITerminalGoodsRepository
{
	TerminalGoods[] TerminalGoods(string[] computers);
}

public class TerminalGoodsRepository : ITerminalGoodsRepository
{
	private readonly MongoDBContext mongo;

	public TerminalGoodsRepository(MongoDBContext mongo)
	{
		this.mongo = mongo;
	}

	public TerminalGoods[] TerminalGoods(string[] computers)
	{
		var tgs = mongo.TerminalGoodsCollection.AsQueryable().Where(o => computers.Length <= 0 || computers.Contains(o.Computer)).ToArray();
		var gIds = tgs.Where(o => o.Goods == null).Select(o => o.GoodsId).Distinct().ToArray();
		var goods = gIds.Any() ? mongo.GoodsCollection.AsQueryable().Where(o => gIds.Contains(o.UniqueId)).ToArray() : new Goods[0];
		foreach (var item in tgs)
		{
			item.Goods = item.Goods ?? goods.FirstOrDefault(f => f.UniqueId == item.GoodsId);
		}
		return tgs;
	}
}