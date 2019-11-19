<Query Kind="Program">
  <Reference>D:\LINQPad\packages\HealthCare.Data.MongoModel.dll</Reference>
  <Reference>D:\LINQPad\packages\HealthCare.Data.MongoModelExtension.dll</Reference>
  <Reference>D:\LINQPad\packages\log4net.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Bson.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Driver.Core.dll</Reference>
  <Reference>D:\LINQPad\packages\MongoDB.Driver.dll</Reference>
  <Reference>D:\LINQPad\packages\Newtonsoft.Json.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>D:\LINQPad\packages\System.Net.Http.Formatting.dll</Reference>
  <Namespace>HealthCare.Data</Namespace>
  <Namespace>MongoDBContext = HealthCare.Data.MongoContext</Namespace>
  <Namespace>MongoDB.Driver</Namespace>
</Query>

private static MongoDBContext mongo = new MongoContext("mongodb://127.0.0.1:27017", "FDZL");

void Main()
{
	// 给所有用户，所有角色授予所有药盒的使用权限
	var cabinets = mongo.CustomerCollection.AsQueryable().SelectMany(c => c.Cabinets).ToArray();
	var virtuals = mongo.CustomerCollection.AsQueryable().SelectMany(c => c.OutOfCabinets).ToArray();
	var nos = cabinets.Concat(virtuals).SelectMany(d => d.Drawers).SelectMany(d => d.Boxes).Select(b => b.No).ToList();

	var users = mongo.UserCollection.AsQueryable().ToArray();
	foreach (var user in users)
	{
		$"{user.Employee?.DisplayName} {user.AvailableStorages.Count} -> {nos.Count}".Dump();
	}
	var rs1 = mongo.UserCollection.UpdateMany(o => true, Builders<User>.Update.Set(o => o.AvailableStorages, nos));
	rs1.Dump();

	var roles = mongo.RoleCollection.AsQueryable().ToArray();
	foreach (var role in roles)
	{
		$"{role?.DisplayName} {role.AvailableStorages.Count} -> {nos.Count}".Dump();
	}
	var rs2 = mongo.RoleCollection.UpdateMany(o => true, Builders<Role>.Update.Set(o => o.AvailableStorages, nos));
	rs2.Dump();
}