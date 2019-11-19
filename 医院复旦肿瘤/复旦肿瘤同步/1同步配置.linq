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
  <Namespace>MongoDB.Bson</Namespace>
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
	解析医嘱();
	// 解析物品();
}

// Define other methods and classes here

void 解析物品()
{
	//	var file = @"D:\LINQPad\dubugs\物品.xml";
	//	var xml = File.ReadAllText(file);
	//	var array = XmlToJObjects(xml);
	//
	//	var xs = array.Select(o => new JObject
	//	{
	//		["source"] = new JObject(),
	//		["target"] = o,
	//	}).ToArray();
	var file = @"D:\LINQPad\dubugs\x.json";
	var json = File.ReadAllText(file);
	var xs = JArray.Parse(json).Select(o => (JObject)o).ToArray();
	AfterSaved(xs);

	object EvalCast(JObject row)
	{
		// 01, 02, 05：西药，草药，中成药
		var v1 = row.GetValue("@ItemClass", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		var v2 = row.GetValue("@ItemCode", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		return $"0{v1} {v2}";
	}

	/// <summary>
	///     data: { source: JObject, target: JObject }[]
	/// </summary>
	dynamic AfterSaved(JObject[] data)
	{
		foreach (var item in data)
		{
			var source = item["source"] as JObject; // xml
			var target = item["target"] as JObject; // ui-object

			// 更新编码 Code
			// 01, 02, 05：西药，草药，中成药
			var uniqueId = target.GetValue("uniqueId", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
			var code = target.GetValue("code", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
			mongo.GoodsCollection.UpdateOne(o => o.UniqueId == uniqueId, Builders<Goods>.Update.Set(o => o.Code, code));
		}
		return null;
	}
}

private JObject[] XmlToJObjects(string xml)
{
	xml.Dump();

	var doc = new XmlDocument();
	doc.LoadXml(xml);
	var json = JsonConvert.SerializeXmlNode(doc, Newtonsoft.Json.Formatting.Indented);
	JToken data = JObject.Parse(json)[doc.DocumentElement.Name];

	var nodes = doc.DocumentElement.ChildNodes.Cast<XmlElement>();
	if (nodes.Select(o => o.Name).Distinct().Count() == 1)
	{
		// array
		data = data[nodes.Select(o => o.Name).First()];
	}

	var array = (data.Type == JTokenType.Array ? (JArray)data : JArray.FromObject(new[] { data })).Select(o => (JObject)o).ToArray();
	return array;
}

void 解析医嘱()
{
	var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "LINQPad Queries\\医院复旦肿瘤\\复旦肿瘤同步\\20190930-prescription.txt");
	var xml = File.ReadAllText(file);
	var array = XmlToJObjects(xml);
	var values = array.Select(row => new
	{
		source = row,
		target = new
		{
			uniqueId = row["@UniqueId"].Value<string>(),
			mode = row["@Mode"]?.ToObject<string>()?.Contains("退药") == true ? ExchangeMode.CheckIn : ExchangeMode.CheckOut,
			departmentSourceId = row["@DepartmentCode"].Value<string>(),
			departmentDestinationId = row["@DepartmentCode"].Value<string>(),
			goodsId = row["@GoodsId"].Value<string>(),
			quantity = row["@Qty"].Value<double>(),
			goodsUnit = row["@UsedUnit"].Value<string>(),
			doctorId = row["@DoctorId"].Value<string>(),
			patientId = row["@PatientId"].Value<string>(),
			issuedTime = row["@IssuedTime"].Value<DateTime>(),
			trackNumber = row["@TrackNumber"].Value<string>(),
			description = row["@Description"].Value<string>(),
			usedFrequency = $"{row["@UsedFrequency"]}({row["@ExecuteTime"]})",
			usedPurpose = row["@UsedPurpose"].Value<string>(),
			usedDosage = row["@UserDosage"].Value<string>(),
			dispensingId = row["@NurseWorkNo"].Value<string>(),
			dispensingTime = DateTime.TryParse(row.GetValue("@DispensingTime", StringComparison.OrdinalIgnoreCase)?.ToObject<string>(), out var value) ? value : DateTime.MaxValue.Date,
			exchangeBarcode = row["@Txm"].Value<string>(),
		},
	}).Select(o => JObject.FromObject(o)).ToArray();

	AfterSaved(values, mongo);

	object EvalCast(JObject row, MongoDBContext mongo)
	{
		//	// 手麻计费需要药品 类号 和 药品代码
		//	var v1 = row.GetValue("@ItemClass", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		//	var v2 = row.GetValue("@ItemCode", StringComparison.OrdinalIgnoreCase)?.ToObject<string>();
		//	return $"0{v1} {v2}";

		// 调拨，有效期和生产日期的转换
		var pds = row["@productDate"]?.ToObject<string>();
		var vds = row["@validDate"]?.ToObject<string>();
		var pd = DateTime.TryParse(pds, out var pdv) ? pdv : (DateTime?)null;
		var vd = DateTime.TryParse(vds, out var vdv) ? vdv : DateTime.MaxValue.Date;
		var offset = Math.Abs((vd.Date - (pd ?? vd).Date).TotalDays);
		return vd.Date.AddSeconds(offset);

		//	// 瓶贴打印    格式：频次(执行时间)
		//	return $"{row["@UsedFrequency"]}({row["@ExecuteTime"]})";	
	}

	/// <summary>
	///     data: { source: JObject, target: JObject }[]
	/// </summary>
	dynamic AfterSaved(JObject[] data, MongoDBContext mongo)
	{
		var cabinets = mongo.CustomerCollection.AsQueryable().Where(o => !o.IsDisabled).SelectMany(c => c.Cabinets)
			.Where(c => c.IsPrimary && !c.DisplayText.Contains("抢救车")).ToArray();
		foreach (var item in data)
		{
			var source = item["source"] as JObject;
			var target = item["target"] as JObject;

			// 更新医嘱二维码
			var uniqueId = source.GetValue("@UniqueId", StringComparison.OrdinalIgnoreCase).ToObject<string>();
			var barcode = source.GetValue("@Txm", StringComparison.OrdinalIgnoreCase).ToObject<string>();
			// 更新 IP
			var department = target.GetValue("departmentDestinationId", StringComparison.OrdinalIgnoreCase).ToObject<string>();
			var computer = cabinets.FirstOrDefault(o => o.DepartmentId == department)?.Computer;

			mongo.PrescriptionCollection.UpdateOne(o => o.UniqueId == uniqueId, Builders<Prescription>.Update
				.Set(o => o.ExchangeBarcode, barcode)
				.Set(o => o.Computer, computer));
		}
		return null;
	}
}