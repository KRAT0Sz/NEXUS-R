namespace KONGOR.MasterServer.Controllers.StorageStatusController;

[ApiController]
[Route("master/storage/status")]
[Consumes("application/x-www-form-urlencoded")]
public class StorageStatusController(MerrickContext databaseContext, IDatabase distributedCache) : ControllerBase
{
    private MerrickContext MerrickContext { get; } = databaseContext;
    private IDatabase DistributedCache { get; } = distributedCache;

    [HttpPost(Name = "Storage Status")]
    public async Task<IActionResult> StorageStatus([FromForm] Dictionary<string, string> formData)
    {
        string? cookie = formData.GetValueOrDefault("cookie");

        if (string.IsNullOrWhiteSpace(cookie))
            return Unauthorized(@"Missing Value For Form Parameter ""cookie""");

        string? accountName = await DistributedCache.GetAccountNameForSessionCookie(cookie);

        if (accountName is null)
            return Unauthorized($@"No Session Found For Cookie ""{cookie}""");

        Account? account = await MerrickContext.Accounts
            .SingleOrDefaultAsync(account => account.Name.Equals(accountName));

        if (account is null)
            return NotFound($@"Account With Name ""{accountName}"" Could Not Be Found");

        // Get cloud storage settings from cache or use defaults
        string cacheKey = $"Storage:CloudSettings:{account.ID}";
        string? cloudSettingsJson = await DistributedCache.StringGetAsync(cacheKey);

        bool useCloud = false;
        bool autoUpload = false;
        string fileModifyTime = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        if (cloudSettingsJson is not null)
        {
            try
            {
                var cloudSettings = System.Text.Json.JsonSerializer.Deserialize<CloudStorageSettings>(cloudSettingsJson);
                if (cloudSettings is not null)
                {
                    useCloud = cloudSettings.UseCloud;
                    autoUpload = cloudSettings.AutoUpload;
                    fileModifyTime = cloudSettings.FileModifyTime;
                }
            }
            catch
            {
                // Use defaults if parsing fails
            }
        }

        // Build PHP serialized response
        Dictionary<string, object> response = new ()
        {
            ["success"] = true,
            ["data"] = new Dictionary<string, object>
            {
                ["account_id"] = account.ID.ToString(),
                ["use_cloud"] = useCloud ? "1" : "0",
                ["cloud_autoupload"] = autoUpload ? "1" : "0",
                ["file_modify_time"] = fileModifyTime
            },
            ["cloud_storage_info"] = new Dictionary<string, object>
            {
                ["account_id"] = account.ID.ToString(),
                ["use_cloud"] = useCloud ? "1" : "0",
                ["cloud_autoupload"] = autoUpload ? "1" : "0",
                ["file_modify_time"] = fileModifyTime
            },
            ["messages"] = string.Empty
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    private class CloudStorageSettings
    {
        public bool UseCloud { get; set; }
        public bool AutoUpload { get; set; }
        public string FileModifyTime { get; set; } = string.Empty;
    }
}
