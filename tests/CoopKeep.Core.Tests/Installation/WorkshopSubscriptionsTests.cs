using CoopKeep.Core.Installation;
using Xunit;

namespace CoopKeep.Core.Tests.Installation;

/// <summary>İki Steam hesaplı bilgisayar: diğer hesabın modları diskte ama oyun onları görmez.</summary>
public class WorkshopSubscriptionsTests
{
    private const string Acf = """
        "AppWorkshop"
        {
            "appid"     "261550"
            "WorkshopItemsInstalled"
            {
                "100" { "size" "1" }
            }
            "WorkshopItemDetails"
            {
                "100"
                {
                    "manifest"      "1"
                    "subscribedby"  "1611356422,483335147"
                }
                "200"
                {
                    "manifest"      "2"
                    "subscribedby"  "1611356422"
                }
                "300"
                {
                    "manifest"      "3"
                }
            }
        }
        """;

    [Fact]
    public void ParseAcf_ReadsSubscribers()
    {
        var map = WorkshopSubscriptions.ParseAcf(Acf);

        Assert.Equal([483335147u, 1611356422u], map["100"].Order());
        Assert.Equal([1611356422u], map["200"]);
        Assert.Empty(map["300"]);
    }

    [Fact]
    public void IsAvailable_OnlyForActiveAccount()
    {
        var root = Path.Combine(Path.GetTempPath(), "ck-ws-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "steamapps", "workshop", "content", "261550");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(root, "steamapps", "workshop", "appworkshop_261550.acf"), Acf);
        try
        {
            var subs = WorkshopSubscriptions.Load([content], activeAccount: 483335147);

            Assert.True(subs.IsAvailable("100"));
            Assert.False(subs.IsAvailable("200")); // yalnızca diğer hesap abone
            Assert.True(subs.IsAvailable("300"));  // kimin olduğu bilinmiyor: engelleme
            Assert.True(subs.IsAvailable("999"));  // kayıtta yok (elle kopyalanmış)
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordedButMissing_FindsDeletedFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ck-ws-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "steamapps", "workshop", "content", "261550");
        Directory.CreateDirectory(Path.Combine(content, "100")); // 100 duruyor, 200 ve 300 silinmiş
        File.WriteAllText(Path.Combine(root, "steamapps", "workshop", "appworkshop_261550.acf"), Acf.Replace(
            "\"100\" { \"size\" \"1\" }", "\"100\" { \"size\" \"1\" }\n                \"200\" { \"size\" \"2\" }"));
        try
        {
            Assert.Equal(["100", "200"], WorkshopSubscriptions.ParseInstalled(File.ReadAllText(Path.Combine(root, "steamapps", "workshop", "appworkshop_261550.acf"))));
            Assert.Equal(["200"], WorkshopSubscriptions.RecordedButMissing([content]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unknown_TreatsEverythingAsAvailable() => Assert.True(WorkshopSubscriptions.Unknown.IsAvailable("200"));
}
