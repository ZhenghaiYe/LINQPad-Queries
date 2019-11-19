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
</Query>

private static MongoDBContext mongo = new MongoContext("mongodb://127.0.0.1:27017", "FDZL");

void Main()
{
	var obj = new JObject { ["?xml"] = new JObject { ["@version"] = "1.0", ["@encoding"] = "gbk" } };
	JToken tmp = obj;
	foreach (var property in "string.rows.row".Split('.'))
	{
		tmp = tmp[property] = new JObject();
	}
	tmp["@Field0"] = "T";
	tmp["@ItemId"] = "3486";
	tmp["@UNIQUE_ID"] = "e9e4375c-606f-40a1-ac1f-2c774f71a2c5";
	JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented).Dump();

	var data = mongo.PrescriptionCollection.AsQueryable().Where(o => !o.IsDisabled).ToArray();
	var values = FilterData(data, mongo);
	values.Select(value => new { build = Build(value), result = Result(obj, value, mongo) }).ToArray().Dump();
}

// Define other methods and classes here


//有多个医嘱计费接口时，过滤数据
static JObject[] FilterData(Prescription[] data, MongoDBContext mongo)
{
	// 精麻标识，精麻一 2，精麻二 10，其余 0
	(string id, string name, string type)[] maps = new[]
	{
		("3486", "[甲]芬太尼针", "2"),
		("4248", "[甲]瑞芬太尼", "2"),
		("4291", "[甲]舒芬太尼", "2"),
		("1924", "[甲]吗啡针", "2"),
		("3489", "[甲]麻黄碱针", "2"),
		("6443", "[甲]注射用盐酸瑞芬太尼", "2"),
		("6337", "[乙20%]盐酸羟考酮注射液（奥诺美）", "2"),
		("6446", "[甲]枸橘酸舒芬太尼注射液", "2"),
		("6725", "[乙20%]酒石酸布托菲诺注射液（诺扬）", "10"),
		("4363", "[甲]咪达唑仑针(力月西)", "10"),
		("6225", "地佐辛注射液（加罗宁）", "10"),
	};

	var computers = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).SelectMany(c => c.Cabinets)
		.Where(c => c.IsPrimary && !c.DisplayText.Contains("抢救车"))
		.Select(c => c.Computer).Distinct().ToArray();
	// 手麻，只上传手术相关的记录
	var values = data.Where(o => computers.Contains(o.Computer) && !string.IsNullOrEmpty(o.MedicationId) && !string.IsNullOrEmpty(o.OperationScheduleId)).OrderBy(o => o.FinishTime).ToArray();

	var keys = values.Select(p => $"{p.UniqueId}:智能药箱数据上传").ToArray();
	var configs = mongo.SystemConfigCollection.AsQueryable().Where(o => keys.Contains(o.Key)).ToArray();

	var outs = values.Where(o => o.Mode == ExchangeMode.CheckOut).Select(p =>
	{
		p.IsSynchronized = configs.Any(x => x.Key == $"{p.UniqueId}:智能药箱数据上传") || p.IsSynchronized;

		var obj = JObject.FromObject(p);
		var kvp = p.Goods?.Code.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		obj["quantity"] = p.QtyActual / (p.Goods?.Conversion ?? 1.0);
		obj["ItemClass"] = kvp?.ElementAtOrDefault(0);  // 01, 02, 05 (西药，草药，中成药)
		obj["ItemCode"] = kvp?.ElementAtOrDefault(1);
		obj["ItemType"] = maps.FirstOrDefault(x => x.id == p.GoodsId).type ?? "0";
		return obj;
	}).ToArray();
	return outs;
}

// 转成 xml 格式，转成 JObject 格式
static dynamic Build(JObject mapping)
{
	var value = new JObject
	{
		["rows"] = JArray.FromObject(new[] { new { row = mapping, } }),
	};
	var json = JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
	var doc = JsonConvert.DeserializeXmlNode(json);
	return doc.InnerXml;
}

// 是否成功，提示信息，对方的医嘱id
static (bool ok, string msg, string id) Result(JObject ret, JObject row, MongoDBContext mongo)
{
	JToken token = ret;
	foreach (var property in "string.rows.row".Split('.'))
	{
		switch (token?.Type)
		{
			case JTokenType.Null:
			case JTokenType.Undefined:
			case JTokenType.None: token = null; break;
		}
		token = token?[property];
	}

	var code = token?["@Field0"]?.ToObject<string>();  // 是否成功
	var msg = token?["@ItemId"]?.ToObject<string>();   // 药品唯一编码
	var id = token?["@UNIQUE_ID"]?.ToObject<string>(); // 手麻收费唯一编码
	if (code == "T")
	{
		// 当手麻计费成功时，记录状态和用于撤销的 UNIQUE_ID
		var uniqueId = row.GetValue("uniqueId", StringComparison.OrdinalIgnoreCase)?.ToObject<string>() ?? row.GetValue("_id", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		var key = $"{uniqueId}:智能药箱数据上传";
		var config = new SystemConfig { UniqueId = key, Key = key, JObject = JsonConvert.SerializeObject(new { Field0 = code, ItemId = msg, UNIQUE_ID = id, }) };
		mongo.SystemConfigCollection.FindOneAndReplace<SystemConfig>(x => x.Key == key, config, new FindOneAndReplaceOptions<SystemConfig, SystemConfig> { IsUpsert = true });
	}
	return (code == "T", msg, id);
}