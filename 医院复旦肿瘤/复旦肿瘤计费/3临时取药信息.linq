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
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipTemporaryUseResponse.return.root.result".Split('.'))
	{
		tmp = tmp[property] = new JObject();
	}
	tmp["@RETCODE"] = "1";
	tmp["@RETMSG"] = "成功";
	JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented).Dump();

	var data = mongo.PrescriptionCollection.AsQueryable().Where(o => !o.IsDisabled).ToArray();
	var values = FilterData(data, mongo);
	values.Select(value => new { build = Build(value), result = Result(obj, value, mongo) }).ToArray().Dump();
}

// Define other methods and classes here


//有多个医嘱计费接口时，过滤数据
static JObject[] FilterData(Prescription[] data, MongoDBContext mongo)
{
	var computers = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).SelectMany(c => c.Cabinets)
		.Where(c => c.IsPrimary && !c.DisplayText.Contains("抢救车"))
		.Select(c => c.Computer).Distinct().ToArray();
	// 平台，临时取药信息
	var values = data.Where(d => computers.Contains(d.Computer) && !string.IsNullOrEmpty(d.MedicationId) && string.IsNullOrEmpty(d.OperationScheduleId) && d.FinishTime != null);
	var presIds = values.Select(r => r.UniqueId).ToArray();
	var acjs = mongo.ActionJournalCollection.AsQueryable().Where(o => presIds.Contains(o.TargetId)).ToArray();

	var keys = values.Select(p => $"{p.UniqueId}:临时取药信息").ToArray();
	var configs = mongo.SystemConfigCollection.AsQueryable().Where(o => keys.Contains(o.Key)).ToArray();

	var outs = values.Select(p =>
	{
		p.IsSynchronized = configs.Any(x => x.Key == $"{p.UniqueId}:临时取药信息") || p.IsSynchronized;

		var acj = acjs.Where(o => o.TargetId == p.UniqueId).LastOrDefault();
		var obj = JObject.FromObject(p);
		obj["@dispensingNo"] = p.UniqueId;
		obj["@wardCode"] = p.DepartmentSourceId;
		obj["@wardName"] = p.DepartmentSource?.DisplayName;
		obj["@equipCode"] = p.Computer;
		obj["@equipName"] = null;
		obj["@sortNo"] = (int)(p.FinishTime.Value - new DateTime(2019, 1, 1)).TotalSeconds;
		obj["@drugId"] = p.GoodsId;
		obj["@drugName"] = p.Goods?.DisplayName;
		obj["@usageCode"] = null;
		obj["@usageUsage"] = null;
		obj["@specification"] = p.Goods?.Specification;
		obj["@mafcName"] = p.Goods?.Manufacturer;
		obj["@batchNo"] = p.BatchNumber;
		obj["@quantity"] = p.QtyActual;
		obj["@quantityUnit"] = p.Goods?.UsedUnit;
		obj["@isAnestheticDrug"] = null;
		obj["@isPsychotropicDrug"] = null;
		obj["@skinTestResultType"] = null;
		obj["@executeNurseWorkNo"] = acj?.PrimaryUserId;
		obj["@executeNurseName"] = acj?.PrimaryUserName;
		obj["@executeTime"] = p.FinishTime?.ToString("yyyy-MM-dd HH:mm:ss");
		return obj;
	}).ToArray();
	return outs;
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
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipTemporaryUseResponse.return.root.result".Split('.'))
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
		// 当平台计费成功时，记录状态
		var uniqueId = row.GetValue("uniqueId", StringComparison.OrdinalIgnoreCase)?.ToObject<string>() ?? row.GetValue("_id", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		var key = $"{uniqueId}:临时取药信息";
		var config = new SystemConfig { UniqueId = key, Key = key, JObject = JsonConvert.SerializeObject(new { RETCODE = code, RETMSG = msg, }) };
		mongo.SystemConfigCollection.FindOneAndReplace<SystemConfig>(x => x.Key == key, config, new FindOneAndReplaceOptions<SystemConfig, SystemConfig> { IsUpsert = true });
	}
	return (code == "1", msg, null);
}