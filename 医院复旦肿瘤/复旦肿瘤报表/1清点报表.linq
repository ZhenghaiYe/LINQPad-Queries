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

private static MongoDBContext mongo = new MongoContext("mongodb://127.0.0.1:27017", "SFRADB");

void Main()
{
	var body = new JObject { ["开始时间"] = "2019-10-01", ["结束时间"] = "2019-10-25", ["柜子"] = "172.100.144.113" };
	日清点报表(body, mongo).Dump();
	月清点报表(body, mongo).Dump();
}

// Define other methods and classes here

object[] 日清点报表(JObject body, MongoDBContext mongo)
{
	var start = DateTime.TryParse(body["开始时间"]?.ToString(), out var s) ? s : new DateTime(DateTime.Now.Year, 1, 1);
	var end = DateTime.TryParse(body["结束时间"]?.ToString(), out var e) ? e : DateTime.Now;
	end = new DateTime(end.Year, end.Month, end.Day, 23, 59, 59);
	var computer = body["柜子"].ToString();
	var notes = mongo.AnyCollection<InventoryNotes>("Inventory.Notes").AsQueryable().Where(n => n.IsDisabled != true && n.CreatedTime >= start && n.CreatedTime < end && n.Computer == computer).ToArray();
	var lastData = notes.Where(n => n.Type == "月清点").GroupBy(n => n.CreatedTime.Month).Select(n => { return n.Last(); }).ToArray();
	notes = notes.Where(n => n.Type != "月清点").Concat(lastData).OrderBy(n => n.CreatedTime).ToArray();
	var users = mongo.UserCollection.AsQueryable();

	var terminalGoods = mongo.TerminalGoodsCollection.AsQueryable().Where(o => o.Computer == computer).ToArray();
	var notesresult = notes.Select(n =>
	{
		StringBuilder str = new StringBuilder();

		n.Goods.ToList().ForEach(l =>
		{
			var t = terminalGoods.FirstOrDefault(o => o.GoodsId == l.GoodsId);
			if (l.State == "缺物")
			{
				str.AppendLine($"{l.GoodsName} 缺 {l.WarnningQty - l.ActualQty} {t.Goods?.UsedUnit}");
			}
			else if (l.State == "缺货")
			{
				// 格式：[名称] [报警基数-现存量] [单位]缺货
				str.AppendLine($"{l.GoodsName} {t.WarningQuota - l.ActualQty} {t.Goods?.UsedUnit}缺货");
			}

		});
		string name = "";
		if (!string.IsNullOrEmpty(n.verifier))
		{
			name = users.FirstOrDefault(m => m.LoginId == n.verifier)?.Employee?.DisplayName ?? n.verifier;
		}
		name = name == null ? "" : name;
		return new
		{
			日期 = n.CreatedTime.ToString("MM/dd"),
			时间 = n.CreatedTime.ToString("HH:mm"),
			状态 = n.State,
			缺漏物品名称 = str.ToString(),
			签名 = users.FirstOrDefault(m => m.LoginId == n.ExecutorId)?.Employee?.DisplayName ?? n.ExecutorId,
			复核人签名 = name,
			备注 = n.Remark ?? "",
			排序 = n.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss"),
		};
	}).ToArray();

	var actions = mongo.ActionJournalCollection.AsQueryable().Where(n => (n.RecordType.EndsWith("预支") || n.RecordType == "急症补录") && n.CreatedTime >= start && n.CreatedTime < end && n.Computer == computer).ToArray();
	var medIds = actions.Select(n => n.TargetId).ToArray();
	var mes = mongo.MedicationCollection.AsQueryable().Where(n => medIds.Contains(n.UniqueId)).ToArray();
	var pres = mongo.PrescriptionCollection.AsQueryable().Where(n => medIds.Contains(n.UniqueId)).ToArray();
	var goods = mongo.GoodsCollection.AsQueryable().ToArray();
	var actiontemps = actions.Select(n =>
	{
		var me = mes.FirstOrDefault(m => m.UniqueId == n.TargetId);
		var pe = pres.FirstOrDefault(p => p.UniqueId == n.TargetId);
		var pa = (me?.Plans ?? pe?.Plans)?.Where(m => m.Box.No == n.No && m.CreatedTime <= n.CreatedTime).OrderByDescending(m => m.CreatedTime).FirstOrDefault();
		return new
		{
			key = pa?.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss") ?? n.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss"),
			action = n,
		};
	}).ToArray();
	var actionsresult = actiontemps.GroupBy(n => n.key).Select(n =>
	{
		StringBuilder str = new StringBuilder();
		n.ToList().ForEach(l =>
		{
			var good = goods.FirstOrDefault(m => m.UniqueId == l.action.GoodsId);
			str.AppendLine(good?.DisplayName + "  缺" + l.action.Qty + good?.UsedUnit);
		});
		var ff = n.FirstOrDefault();
		return new
		{
			日期 = DateTime.Parse(n.Key).ToString("MM/dd"),
			时间 = DateTime.Parse(n.Key).ToString("HH:mm"),
			状态 = "缺物",
			缺漏物品名称 = str.ToString(),
			签名 = ff.action.OperatorUserName,
			复核人签名 = "",
			备注 = "取药",
			排序 = n.Key,
		};
	}).ToArray();
	var result = notesresult.Concat(actionsresult).OrderBy(n => n.排序).ToArray();
	return result;
}

object 月清点报表(JObject body, MongoDBContext monog)
{
	int year = DateTime.Now.Year;
	DateTime dateTime = DateTime.Parse(year + "-1-1");
	var computer = body["柜子"].ToString();

	var notes = mongo.AnyCollection<InventoryNotes>("Inventory.Notes").AsQueryable().Where(n => n.Computer == computer && n.Type == "月清点" && n.CreatedTime >= dateTime).ToArray();
	DateTime d1 = new DateTime(DateTime.Now.Year - 1, 12, 1);
	DateTime d2 = new DateTime(DateTime.Now.Year - 1, 12, 31, 23, 59, 59);
	var note = mongo.AnyCollection<InventoryNotes>("Inventory.Notes").AsQueryable().Where(n => n.Computer == computer && n.Type == "月清点" && n.CreatedTime >= d1 && n.CreatedTime < d2).ToArray();
	var users = mongo.UserCollection.AsQueryable();
	var monthdata11 = notes.GroupBy(n => n.CreatedTime.Month).Select(n => { return n.Last(); }).ToArray();
	var monthdata = monthdata11.Select(n => new
	{
		Executor = users.FirstOrDefault(m => m.LoginId == n.ExecutorId)?.Employee.DisplayName ?? n.ExecutorId,
		Verifier = users.FirstOrDefault(m => m.LoginId == n.verifier)?.Employee.DisplayName ?? n.verifier,
		data = n
	}).ToArray();

	Dictionary<string, string> qtyRow = new Dictionary<string, string>();
	qtyRow.Add("[甲]地塞米松磷酸钠注射液（辰欣）", "6219");
	qtyRow.Add("[甲]氢化可的松琥珀酸钠针", "4140");
	qtyRow.Add("[甲]去乙酰毛花苷注射液（西地兰）", "6425"); qtyRow.Add("[甲]盐酸利多卡因注射液(山东华鲁)", "6288"); qtyRow.Add("[甲]多巴胺针", "1985"); qtyRow.Add("[乙10%]去氧肾上腺素针(苯肾上腺素", "1984");
	qtyRow.Add("[甲]重酒石酸间羟胺注射液", "6435"); qtyRow.Add("[甲]去甲肾上腺素针", "1982"); qtyRow.Add("[甲]肾上腺素针", "1981"); qtyRow.Add("[甲]硫酸阿托品注射液", "6490"); qtyRow.Add("[甲]异丙肾上腺素针", "1983"); qtyRow.Add("[甲]盐酸洛贝林注射液", "4966");
	qtyRow.Add("[甲]尼可刹米针(可拉明)", "3142"); qtyRow.Add("常规静脉盘", "1"); qtyRow.Add("一次性针筒10ml", "2"); qtyRow.Add("一次性针筒5ml", "3");
	qtyRow.Add("输液皮条", "4"); qtyRow.Add("输血皮条", "5"); qtyRow.Add("排气管", "6"); qtyRow.Add("长头针", "7");
	qtyRow.Add("静脉留置针", "8"); qtyRow.Add("头皮针 ", "9"); qtyRow.Add("伤口薄膜", "10"); qtyRow.Add("静脉帖", "11"); qtyRow.Add("缝线", "12"); qtyRow.Add("刀片", "13");
	qtyRow.Add("无菌手套", "14"); qtyRow.Add("急救包", "15"); qtyRow.Add("静切包", "19"); qtyRow.Add("气切包", "20"); qtyRow.Add("8号一次性气切套管带气囊", "21"); qtyRow.Add("持物钳", "28");
	qtyRow.Add("金属气筒管", "29"); qtyRow.Add("[甲]葡萄糖针(塑袋)", "3208"); qtyRow.Add("[甲]氯化钠针(塑袋）", "3218"); qtyRow.Add("※[甲]羟乙基淀粉氯化钠注射液(万汶)", "4392"); qtyRow.Add("手电筒带电池", "22"); qtyRow.Add("电池", "23"); qtyRow.Add("5米接线板", "24");
	qtyRow.Add("1.8米接线板", "25"); qtyRow.Add("血压计", "26"); qtyRow.Add("听诊器", "27");

	var qtyData = qtyRow.Values.Select(n =>
	{
		return new
		{
			一月 = getQty(1, n),
			二月 = getQty(2, n),
			三月 = getQty(3, n),
			四月 = getQty(4, n),
			五月 = getQty(5, n),
			六月 = getQty(6, n),
			七月 = getQty(7, n),
			八月 = getQty(8, n),
			九月 = getQty(9, n),
			十月 = getQty(10, n),
			十一月 = getQty(11, n),
			十二月 = getQty(12, n),
		};
	}).ToList();

	List<string> authRow = new List<string>() { "清点人", "检查者", "护士长" };
	var auth = authRow.Select(n =>
	{
		if (n == "清点人")
		{
			return new
			{
				一月 = getExecutor(1, n),
				二月 = getExecutor(2, n),
				三月 = getExecutor(3, n),
				四月 = getExecutor(4, n),
				五月 = getExecutor(5, n),
				六月 = getExecutor(6, n),
				七月 = getExecutor(7, n),
				八月 = getExecutor(8, n),
				九月 = getExecutor(9, n),
				十月 = getExecutor(10, n),
				十一月 = getExecutor(11, n),
				十二月 = getExecutor(12, n),
			};
		}
		else if (n == "检查者")
		{
			return new
			{
				一月 = getVerifier(1, n),
				二月 = getVerifier(2, n),
				三月 = getVerifier(3, n),
				四月 = getVerifier(4, n),
				五月 = getVerifier(5, n),
				六月 = getVerifier(6, n),
				七月 = getVerifier(7, n),
				八月 = getVerifier(8, n),
				九月 = getVerifier(9, n),
				十月 = getVerifier(10, n),
				十一月 = getVerifier(11, n),
				十二月 = getVerifier(12, n),
			};
		}
		else
		{
			return new
			{
				一月 = get护士长(1),
				二月 = get护士长(2),
				三月 = get护士长(3),
				四月 = get护士长(4),
				五月 = get护士长(5),
				六月 = get护士长(6),
				七月 = get护士长(7),
				八月 = get护士长(8),
				九月 = get护士长(9),
				十月 = get护士长(10),
				十一月 = get护士长(11),
				十二月 = get护士长(12),
			};
		}
	}).ToList();

	var biaotou = new
	{
		一月 = get表头(1),
		二月 = get表头(2),
		三月 = get表头(3),
		四月 = get表头(4),
		五月 = get表头(5),
		六月 = get表头(6),
		七月 = get表头(7),
		八月 = get表头(8),
		九月 = get表头(9),
		十月 = get表头(10),
		十一月 = get表头(11),
		十二月 = get表头(12),
	};
	qtyData.Insert(0, biaotou);

	var jishu = new
	{
		一月 = "",
		二月 = "",
		三月 = "",
		四月 = "",
		五月 = "",
		六月 = "",
		七月 = "",
		八月 = "",
		九月 = "",
		十月 = "",
		十一月 = "",
		十二月 = "",
	};
	qtyData.Insert(1, jishu);

	List<string> guiwaiGoods = new List<string>() { "污物桶", "氧气瓶", "利器盒", "氧气湿化瓶", "扩展桌面", "心肺复苏按压板" };
	var guiwai = guiwaiGoods.Select(n =>
	{
		return new
		{
			一月 = !string.IsNullOrEmpty(auth[0].一月) ? "1" : "",
			二月 = !string.IsNullOrEmpty(auth[0].二月) ? "1" : "",
			三月 = !string.IsNullOrEmpty(auth[0].三月) ? "1" : "",
			四月 = !string.IsNullOrEmpty(auth[0].四月) ? "1" : "",
			五月 = !string.IsNullOrEmpty(auth[0].五月) ? "1" : "",
			六月 = !string.IsNullOrEmpty(auth[0].六月) ? "1" : "",
			七月 = !string.IsNullOrEmpty(auth[0].七月) ? "1" : "",
			八月 = !string.IsNullOrEmpty(auth[0].八月) ? "1" : "",
			九月 = !string.IsNullOrEmpty(auth[0].九月) ? "1" : "",
			十月 = !string.IsNullOrEmpty(auth[0].十月) ? "1" : "",
			十一月 = !string.IsNullOrEmpty(auth[0].十一月) ? "1" : "",
			十二月 = !string.IsNullOrEmpty(auth[0].十二月) ? "1" : "",
		};
	}).ToArray();
	return (qtyData.Concat(guiwai).Concat(auth)).ToArray();

	string get表头(int yue)
	{
		var data = monthdata.FirstOrDefault(m => m.data?.CreatedTime.Month == yue); if (data != null)
		{
			return data.data.CreatedTime.ToString("MM/dd");
		}
		return "";
	}
	string getQty(int yue, string goodId)
	{
		return monthdata.FirstOrDefault(m => m.data?.CreatedTime.Month == yue)?.data.Goods.FirstOrDefault(l => l.GoodsId == goodId)?.ActualQty.ToString();
	}
	string getExecutor(int yue, string goodId)
	{
		return monthdata.FirstOrDefault(m => m.data?.CreatedTime.Month == yue)?.Executor;
	}
	string getVerifier(int yue, string goodId)
	{
		return monthdata.FirstOrDefault(m => m.data?.CreatedTime.Month == yue)?.Verifier;
	}
	string get护士长(int yue)
	{
		var lq = mongo.AnyCollection<InventoryNotes>("Inventory.Notes").AsQueryable().Where(l => l.Computer == computer && l.IsDisabled != true && l.Type == "护士长质控");
		string name = ""; var thisYue = monthdata.FirstOrDefault(m => m.data.CreatedTime.Month == yue);
		var nextYue = monthdata.FirstOrDefault(m => m.data.CreatedTime.Month == yue + 1);
		if (thisYue != null && nextYue != null)
		{
			var s = thisYue.data.CreatedTime; var e = nextYue.data.CreatedTime;
			var hu2 = lq.Where(l => l.CreatedTime >= s && l.CreatedTime < e).OrderByDescending(n => n.CreatedTime).ToArray();
			if (hu2.Length > 0)
			{
				name = users.FirstOrDefault(m => m.LoginId == hu2[0].verifier)?.Employee.DisplayName ?? hu2[0].verifier;
			}
		}
		else if (thisYue != null)
		{
			var hu1 = lq.Where(l => l.CreatedTime >= thisYue.data.CreatedTime && l.CreatedTime <= DateTime.Now).OrderByDescending(n => n.CreatedTime).ToArray();
			if (hu1.Length > 0)
			{
				name = users.FirstOrDefault(m => m.LoginId == hu1[0].verifier)?.Employee.DisplayName ?? hu1[0].verifier;
			}
		}
		return name;
	}
}