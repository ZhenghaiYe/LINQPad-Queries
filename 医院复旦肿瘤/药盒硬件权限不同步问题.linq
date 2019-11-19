<Query Kind="Program">
  <Reference Relative="..\LinqPad\packages\HealthCare.Data.MongoModel.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\HealthCare.Data.MongoModel.dll</Reference>
  <Reference Relative="..\LinqPad\packages\HealthCare.Data.MongoModelExtension.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\HealthCare.Data.MongoModelExtension.dll</Reference>
  <Reference Relative="..\LinqPad\packages\log4net.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\log4net.dll</Reference>
  <Reference Relative="..\LinqPad\packages\MongoDB.Bson.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\MongoDB.Bson.dll</Reference>
  <Reference Relative="..\LinqPad\packages\MongoDB.Driver.Core.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\MongoDB.Driver.Core.dll</Reference>
  <Reference Relative="..\LinqPad\packages\MongoDB.Driver.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\MongoDB.Driver.dll</Reference>
  <Reference Relative="..\LinqPad\packages\Newtonsoft.Json.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\Newtonsoft.Json.dll</Reference>
  <Reference Relative="..\LinqPad\packages\Oracle.ManagedDataAccess.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\Oracle.ManagedDataAccess.dll</Reference>
  <Reference Relative="..\LinqPad\packages\Oracle.ManagedDataAccess.EntityFramework.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\Oracle.ManagedDataAccess.EntityFramework.dll</Reference>
  <Reference Relative="..\LinqPad\packages\System.Net.Http.Formatting.dll">E:\GitHub\LINQPad-Queries\LinqPad\packages\System.Net.Http.Formatting.dll</Reference>
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

private static MongoDBContext mongo = new MongoContext("mongodb://127.0.0.1:27017", "2019-11-18-02(肿瘤东院数据库)");
private static MongoDBContext mongo2 = new MongoContext("mongodb://127.0.0.1:27017", "5B抢救车2019-11-18-01");
void Main()
{
	var cabinets = mongo.CustomerCollection.AsQueryable().SelectMany(c => c.Cabinets).ToList();
	var idx = cabinets.FindIndex(c => c.DisplayText.Contains("5B病区抢救车")).Dump();
	var mm = cabinets[idx].Drawers.SelectMany(d => d.Boxes).Select(b => new { b.DisplayText, b.No, }).ToArray();

	var cabinets2 = mongo2.CustomerCollection.AsQueryable().SelectMany(c => c.Cabinets).ToList();
	var nn = cabinets2.SelectMany(c => c.Drawers).SelectMany(d => d.Boxes).Select(b => new { b.DisplayText, b.No }).ToArray();

	var count = Math.Max(mm.Count(), nn.Count());
	Enumerable.Range(0, count).Select(ix =>
	{
		var m = mm.ElementAtOrDefault(ix);
		var n = nn.ElementAtOrDefault(ix);
		return new { mNo = m?.No, mName = m?.DisplayText, nNo = n?.No, nName = n?.DisplayText };
	}).Dump();

	cabinets[idx].Drawers.Select(d => new { d.No, d.DisplayText, d.MaxColumn, d.MaxRow }).Dump("服务器配置");
	cabinets2.SelectMany(c => c.Drawers).Select(d => new { d.No, d.DisplayText, d.MaxColumn, d.MaxRow }).Dump("抢救车配置");
}