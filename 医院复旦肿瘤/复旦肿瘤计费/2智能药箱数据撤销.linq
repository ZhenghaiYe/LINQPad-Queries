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
	// 手麻，只上传手术相关的记录
	var values = data.Where(o => computers.Contains(o.Computer) && !string.IsNullOrEmpty(o.MedicationId) && !string.IsNullOrEmpty(o.OperationScheduleId)).OrderBy(o => o.FinishTime).ToArray();

	var keys = values.Select(p => $"{p.UniqueId}:智能药箱数据{(p.Mode == ExchangeMode.CheckIn ? "撤销" : "上传")}").ToArray();
	var configs = mongo.SystemConfigCollection.AsQueryable().Where(o => keys.Contains(o.Key)).ToArray();

	var ins = values.Where(o => o.Mode == ExchangeMode.CheckIn).Select(p =>
	{
		p.IsSynchronized = configs.Any(x => x.Key == $"{p.UniqueId}:智能药箱数据撤销") || p.IsSynchronized;

		// 找到产生退药医嘱的取药医嘱
		// 1. 退药医嘱之前执行，同一手术排班，取药
		// 2. 医生、患者、物品、数量全等
		// 3. 按照执行时间升排后最后一条
		var outOne = values.LastOrDefault(x =>
			x.FinishTime <= p.FinishTime && x.OperationScheduleId == p.OperationScheduleId && x.Mode == ExchangeMode.CheckOut
			&& x.DoctorId == p.DoctorId && x.PatientId == p.PatientId && x.GoodsId == p.GoodsId && Math.Abs(x.QtyActual) == Math.Abs(p.QtyActual)
		);
		// 获取取药医嘱的“手麻唯一编码”，用于撤销
		var outResult = configs.FirstOrDefault(x => x.Key == $"{outOne?.UniqueId}:智能药箱数据上传")?.JObject?.ToString() ?? "{}";

		var obj = JObject.FromObject(p);
		obj["UNIQUE_ID"] = JObject.Parse(outResult).GetValue("UNIQUE_ID", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		obj["AnesDoctor"] = p.DoctorId;
		obj["RecordDateTime"] = p.FinishTime?.ToString("yyyy-MM-dd HH:mm:ss");
		return obj;
	}).ToArray();
	return ins;
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
	return doc.OuterXml;
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

	var code = token?["@Field0"]?.ToObject<string>();
	if (code == "T")
	{
		// 当手麻撤销计费时，记录状态
		var uniqueId = row.GetValue("uniqueId", StringComparison.OrdinalIgnoreCase)?.ToObject<string>() ?? row.GetValue("_id", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		var key = $"{uniqueId}:智能药箱数据撤销";
		var config = new SystemConfig { UniqueId = key, Key = key, JObject = JsonConvert.SerializeObject(new { Field0 = code, }) };
		mongo.SystemConfigCollection.FindOneAndReplace<SystemConfig>(x => x.Key == key, config, new FindOneAndReplaceOptions<SystemConfig, SystemConfig> { IsUpsert = true });
	}
	return (code == "T", code, null);
}