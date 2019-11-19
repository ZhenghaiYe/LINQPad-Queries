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
	var obj = new JObject();
	JToken tmp = obj;
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipReplenishExcuteResponse.return.root.result".Split('.'))
	{
		tmp = tmp[property] = new JObject();
	}
	tmp["@RETCODE"] = "1";
	tmp["@RETMSG"] = "成功";

	var values = FilterData(null, mongo);
	values.Select(value => new { build = Build(value), result = Result(obj, value, mongo) }).ToArray().Dump();
}

// Define other methods and classes here


//有多个医嘱计费接口时，过滤数据
static JObject[] FilterData(Prescription[] data, MongoDBContext mongo)
{
	// 调用平台接口，反馈已经执行的调拨
	var lq = mongo.AllocationCollection.AsQueryable().Where(o => !o.IsDisabled && o.FinishTime != null);
	if (!lq.Any())
	{
		return new JObject[0];
	}

	var prefix = "Allocation:Feedback:";
	var pIds = lq.Select(o => o.UniqueId).ToArray();
	var acjs = mongo.ActionJournalCollection.AsQueryable().Where(o => pIds.Contains(o.TargetId)).ToArray();
	var s = mongo.SystemConfigCollection.AsQueryable().Where(o => o.Key.StartsWith(prefix)).OrderByDescending(o => o.Key).FirstOrDefault()?.Key.Replace(prefix, "");
	s = string.IsNullOrEmpty(s) ? "2019-01-01" : s;
	var date = DateTime.Parse(s).AddDays(-7);   // 规则；最近七天 -7，最近三十天 -30
	var keys = lq.Where(o => o.FinishTime >= date).Select(o => o.FinishTime).ToArray().Select(t => $"{prefix}{t:yyyy-MM-dd}").Distinct().ToArray();
	var configs = mongo.SystemConfigCollection.AsQueryable().Where(c => keys.Contains(c.Key)).ToArray();
	var callIds = configs.SelectMany(d => d?.JObject?.ToString()?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0]).ToArray();

	var values = lq.Where(o => o.FinishTime >= date && !callIds.Contains(o.UniqueId)).ToArray().Select(p =>
	{
		var acj = acjs.FirstOrDefault(o => o.TargetId == p.UniqueId);
		var obj = JObject.FromObject(p);
		obj["@replenishNo"] = p.ApplyId;
		obj["@drugId"] = p.GoodsId;
		obj["@drugName"] = p.Goods?.DisplayName;
		obj["@specification"] = p.Goods?.Specification;
		obj["@mafcName"] = p.Goods?.Manufacturer;
		obj["@batchNo"] = p.BatchNumber;
		obj["@validDate"] = p.ExpiredDate.ToString("yyyy-MM-dd");
		obj["@quantity"] = p.QtyActual;
		obj["@quantityUnit"] = p.Goods?.UsedUnit;
		obj["@wardCode"] = p.DepartmentDestinationId;
		obj["@wardName"] = p.DepartmentDestination?.DisplayName;
		obj["@equipCode"] = p.Computer;
		obj["@equipName"] = null;
		obj["@inStockTime"] = p.FinishTime?.ToString("yyyy-MM-dd HH:mm:ss");
		obj["@inStockerWorkNo"] = acj?.PrimaryUserId;
		obj["@inStockerName"] = acj?.PrimaryUserName;
		obj["@inStockRemark"] = "";
		obj["@uniqueId"] = p.UniqueId;
		return obj;
	}).ToArray();
	return values;
}

// 转成 xml 格式，转成 JObject 格式
static dynamic Build(JObject mapping)
{
	var value = new JObject
	{
		["?xml"] = JObject.Parse("{\"@version\": \"1.0\", \"@encoding\": \"utf-8\"}"),
		["root"] = new JObject { ["item"] = mapping, }
	};
	var json = JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
	var doc = JsonConvert.DeserializeXmlNode(json);
	return doc.OuterXml.Replace("<", "&lt;").Replace(">", "&gt;");
}

// 是否成功，提示信息，对方的医嘱id
static (bool ok, string msg, string id) Result(JObject ret, JObject row, MongoDBContext mongo)
{
	JToken token = ret;
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipReplenishExcuteResponse.return.root.result".Split('.'))
	{
		switch (token?.Type)
		{
			case JTokenType.Null:
			case JTokenType.Undefined:
			case JTokenType.None: token = null; break;
		}
		token = token?[property];
	}

	var code = token?["@RETCODE"]?.ToObject<string>();
	var msg = token?["@RETMSG"]?.ToObject<string>();
	if (code == "1")
	{
		// 调拨反馈
		var uniqueId = row["_id"].ToObject<string>();
		var date = row.GetValue("finishTime", StringComparison.OrdinalIgnoreCase).ToObject<DateTime>().ToString("yyyy-MM-dd");
		var key = $"Allocation:Feedback:{date}";
		var config = mongo.SystemConfigCollection.AsQueryable().FirstOrDefault(o => o.Key == key) ?? new SystemConfig { Key = key, JObject = "" };
		var ids = (config.JObject?.ToString() ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		config.JObject = string.Join(",", ids.Concat(new[] { uniqueId }).Distinct());
		mongo.SystemConfigCollection.FindOneAndReplace<SystemConfig>(x => x.Key == key, config, new FindOneAndReplaceOptions<SystemConfig, SystemConfig> { IsUpsert = true });
	}
	return (code == "1", msg, null);
}