<Query Kind="Program">
  <Reference>D:\LINQPad\packages\HealthCare.Data.MongoModel.dll</Reference>
  <Reference>D:\LINQPad\packages\HealthCare.Data.MongoModelExtension.dll</Reference>
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
  <Namespace>MongoDB.Bson.Serialization.Attributes</Namespace>
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
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipCheckRecordResponse.return.root.result".Split('.'))
	{
		tmp = tmp[property] = new JObject();
	}
	tmp["@RETCODE"] = "1";
	tmp["@RETMSG"] = "成功";

	var values = FilterData(null, mongo);
	values.Select(value => new { build = Build(value), result = Result(obj, value, mongo) }).ToArray().Dump();
}

// Define other methods and classes here

static JObject[] FilterData(Prescription[] data, MongoDBContext mongo)
{
	// 反馈清点完成的记录
	var computers = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).SelectMany(c => c.Cabinets)
		.Where(c => c.IsPrimary && !c.DisplayText.Contains("抢救车"))
		.Select(c => c.Computer).Distinct().ToArray();

	var flag = new DateTime(2019, 1, 1);
	var lq = mongo.AnyCollection<InventoryNotes>("Inventory.Notes").AsQueryable().Where(t => computers.Contains(t.Computer) && !t.IsDisabled && t.FinishTime >= flag);
	if (!lq.Any())
	{
		return new JObject[0];
	}

	var prefix = "InventoryTransfer:Feedback:";
	var s = mongo.SystemConfigCollection.AsQueryable().Where(o => o.Key.StartsWith(prefix)).OrderByDescending(o => o.Key).FirstOrDefault()?.Key.Replace(prefix, "");
	s = string.IsNullOrEmpty(s) ? flag.ToString("yyyy-MM-dd") : s;
	var date = DateTime.Parse(s).AddDays(-7);   // 规则；最近七天 -7，最近三十天 -30
	var keys = lq.Where(o => o.FinishTime >= date).Select(o => o.FinishTime).ToArray().Select(t => $"{prefix}{t:yyyy-MM-dd}").Distinct().ToArray();
	var configs = mongo.SystemConfigCollection.AsQueryable().Where(c => keys.Contains(c.Key)).ToArray();
	var callIds = configs.SelectMany(d => d?.JObject?.ToString()?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0]).ToArray();

	var values = lq.Where(o => o.FinishTime >= date && !callIds.Contains(o.UniqueId)).ToArray().SelectMany(notes =>
	{
		var gIds = notes.NoteGoods.Where(o => o.EstimateQty != o.ActualQty).Select(n => n.UniqueId).Distinct().ToArray();
		var gs = mongo.GoodsCollection.AsQueryable().Where(o => gIds.Contains(o.UniqueId)).ToArray();
		return notes.NoteGoods.Where(o => o.EstimateQty != o.ActualQty).Select(x =>
		{
			var validDate = x.ExpiredDate;
			var offset = Math.Abs((validDate - validDate.Date).TotalSeconds);
			var g = gs.FirstOrDefault(m => m.UniqueId == x.UniqueId);
			return new JObject
			{
				["_id"] = notes.UniqueId,
				["finishTime"] = notes.FinishTime,
				["@drugId"] = x.UniqueId,
				["@drugName"] = g?.DisplayName,
				["@specification"] = g?.Specification,
				["@mafcName"] = g?.Manufacturer,
				["@batchNo"] = x.BatchNumber,
				["@productDate"] = dateStr(validDate.AddDays(-offset)),
				["@validDate"] = dateStr(validDate.Date),
				["@beforeQuantity"] = x.EstimateQty,
				["@currentQuantity"] = x.ActualQty,
				["@quantityUnit"] = g?.UsedUnit,
				["@wardCode"] = notes.DepartmentId,
				["@wardName"] = null,
				["@equipCode"] = notes.Computer,
				["@equipName"] = null,
				["@checkTime"] = notes.FinishTime?.ToString("yyyy-MM-dd HH:mm:ss"),
				["@remark"] = "盘点结果反馈",
			};
		});
	}).ToArray();
	return values;

	string dateStr(DateTime dt) => dt.Date == DateTime.MaxValue.Date ? "" : dt.ToString("yyyy-MM-dd");
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
	foreach (var property in "soap:Envelope.soap:Body.ns2:equipCheckRecordResponse.return.root.result".Split('.'))
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
		// 清点完成反馈
		var uniqueId = (row.GetValue("_id", StringComparison.OrdinalIgnoreCase) ?? row.GetValue("uniqueId", StringComparison.OrdinalIgnoreCase))?.ToObject<string>();
		var date = row.GetValue("finishTime", StringComparison.OrdinalIgnoreCase).ToObject<DateTime>().ToString("yyyy-MM-dd");
		var key = $"InventoryTransfer:Feedback:{date}";
		var config = mongo.SystemConfigCollection.AsQueryable().FirstOrDefault(o => o.Key == key) ?? new SystemConfig { Key = key, JObject = "" };
		var ids = (config.JObject?.ToString() ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		config.JObject = string.Join(",", ids.Concat(new[] { uniqueId }).Distinct());
		mongo.SystemConfigCollection.FindOneAndReplace<SystemConfig>(x => x.Key == key, config, new FindOneAndReplaceOptions<SystemConfig, SystemConfig> { IsUpsert = true });
	}
	return (code == "1", msg, null);
}