namespace MERRICK.DatabaseContext.Services;

public class DatabaseInitializer(IServiceProvider serviceProvider, ILogger<DatabaseInitializer> logger) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";

    private readonly ActivitySource _activitySource = new (ActivitySourceName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceProvider.CreateScope();

        MerrickContext context = scope.ServiceProvider.GetRequiredService<MerrickContext>();

        await InitializeDatabaseAsync(context, cancellationToken);
    }

    private async Task InitializeDatabaseAsync(MerrickContext context, CancellationToken cancellationToken)
    {
        const string activityName = "Initializing MERRICK Database";

        using Activity? activity = _activitySource.StartActivity(activityName, ActivityKind.Client);

        Stopwatch stopwatch = Stopwatch.StartNew();

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        // Check if database can be connected - if not, create it with EnsureCreated
        // If it can connect but has no migrations applied, use EnsureCreated to create tables
        bool canConnect = false;
        IReadOnlyList<string> appliedMigrations = [];

        try
        {
            canConnect = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            if (canConnect)
            {
                appliedMigrations = (IReadOnlyList<string>)await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Database doesn't exist, will be created below
        }

        // If no migrations have been applied, use EnsureCreated to create the database and tables
        // This handles the case where database exists but tables don't (e.g., created by EnsureCreated previously)
        if (appliedMigrations.Count == 0)
        {
            if (canConnect == false)
            {
                // Database doesn't exist - create it first with EnsureCreated
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Database exists but no migrations - use EnsureCreated to create tables
                // Then run MigrateAsync to record the migration
                await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                await strategy.ExecuteAsync(context.Database.MigrateAsync, cancellationToken);
            }
        }
        else
        {
            await strategy.ExecuteAsync(context.Database.MigrateAsync, cancellationToken);
        }

        await SeedAsync(context, cancellationToken);

        logger.LogInformation("Database Initialization Completed After {ElapsedMilliseconds}ms", stopwatch.ElapsedMilliseconds);
    }

    private async Task SeedAsync(MerrickContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Seeding Database");

        await SeedDataHandlers.SeedUsers(context, cancellationToken, logger);
        await SeedDataHandlers.SeedClans(context, cancellationToken, logger);
        await SeedDataHandlers.SeedAccounts(context, cancellationToken, logger);
        await SeedDataHandlers.SeedHeroGuides(context, cancellationToken, logger);
    }
}
