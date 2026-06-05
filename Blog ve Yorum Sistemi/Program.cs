using BireyselHesaplar.Data;
using BireyselHesaplar.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var runSchemaSyncAtStartup = builder.Configuration.GetValue("Database:RunSchemaSyncAtStartup", true);
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection bulunamadi.");

builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 20 * 1024 * 1024;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();
app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (!runSchemaSyncAtStartup)
    {
        logger.LogInformation("Database baslangic schema sync kapali. Database:RunSchemaSyncAtStartup=false");
    }
    else
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            db.Database.Migrate();
            db.Database.ExecuteSqlRaw(@"
        IF COL_LENGTH('dbo.Users', 'Role') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Users]
            ADD [Role] nvarchar(max) NOT NULL
            CONSTRAINT [DF_Users_Role] DEFAULT N'User' WITH VALUES;
        END

        IF COL_LENGTH('dbo.Users', 'ProfileImageUrl') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Users]
            ADD [ProfileImageUrl] nvarchar(max) NULL;
        END

        IF OBJECT_ID(N'[dbo].[Users]', N'U') IS NOT NULL
           AND OBJECT_ID(N'[bloghanem_user].[Users]', N'U') IS NOT NULL
        BEGIN
            SET IDENTITY_INSERT [dbo].[Users] ON;

            INSERT INTO [dbo].[Users] ([Id], [FullName], [UserName], [Email], [PasswordHash], [Role], [ProfileImageUrl], [CreatedAt])
            SELECT s.[Id],
                   s.[FullName],
                   s.[UserName],
                   s.[Email],
                   s.[PasswordHash],
                   COALESCE(NULLIF(s.[Role], N''), N'User'),
                   s.[ProfileImageUrl],
                   ISNULL(s.[CreatedAt], GETDATE())
            FROM [bloghanem_user].[Users] s
            WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Users] d WHERE d.[Id] = s.[Id])
              AND NOT EXISTS (SELECT 1 FROM [dbo].[Users] d WHERE d.[UserName] = s.[UserName] OR d.[Email] = s.[Email]);

            SET IDENTITY_INSERT [dbo].[Users] OFF;

            DECLARE @maxUserId int = (SELECT ISNULL(MAX([Id]), 0) FROM [dbo].[Users]);
            DBCC CHECKIDENT ('[dbo].[Users]', RESEED, @maxUserId);
        END

        IF OBJECT_ID('UserBans', 'U') IS NULL
        BEGIN
            CREATE TABLE [UserBans] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [UserId] int NOT NULL,
                [AdminUserId] int NOT NULL,
                [Reason] nvarchar(300) NOT NULL,
                [ExpiresAt] datetime2 NOT NULL,
                [CreatedAt] datetime2 NOT NULL
            );

            ALTER TABLE [UserBans]
                ADD CONSTRAINT [FK_UserBans_Users_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE;

            ALTER TABLE [UserBans]
                ADD CONSTRAINT [FK_UserBans_Users_AdminUserId]
                FOREIGN KEY ([AdminUserId]) REFERENCES [Users]([Id]) ON DELETE NO ACTION;

            CREATE INDEX [IX_UserBans_UserId_ExpiresAt] ON [UserBans]([UserId], [ExpiresAt]);
        END

        IF OBJECT_ID('PasswordResetTokens', 'U') IS NULL
        BEGIN
            CREATE TABLE [PasswordResetTokens] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [UserId] int NOT NULL,
                [TokenHash] nvarchar(128) NOT NULL,
                [ExpiresAt] datetime2 NOT NULL,
                [UsedAt] datetime2 NULL,
                [CreatedAt] datetime2 NOT NULL
            );

            ALTER TABLE [PasswordResetTokens]
                ADD CONSTRAINT [FK_PasswordResetTokens_Users_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_PasswordResetTokens_UserId_ExpiresAt' AND [object_id] = OBJECT_ID('PasswordResetTokens'))
        BEGIN
            CREATE INDEX [IX_PasswordResetTokens_UserId_ExpiresAt] ON [PasswordResetTokens]([UserId], [ExpiresAt]);
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_PasswordResetTokens_TokenHash' AND [object_id] = OBJECT_ID('PasswordResetTokens'))
        BEGIN
            CREATE UNIQUE INDEX [IX_PasswordResetTokens_TokenHash] ON [PasswordResetTokens]([TokenHash]);
        END

        IF OBJECT_ID('Categories', 'U') IS NULL
        BEGIN
            CREATE TABLE [Categories] (
                [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Name] nvarchar(80) NOT NULL,
                [Slug] nvarchar(80) NOT NULL,
                [ParentCategoryId] int NULL,
                [CreatedAt] datetime2 NOT NULL
            );
        END

        IF COL_LENGTH('Categories', 'ParentCategoryId') IS NULL
        BEGIN
            ALTER TABLE [Categories]
            ADD [ParentCategoryId] int NULL;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_Categories_Slug' AND [object_id] = OBJECT_ID('Categories'))
        BEGIN
            CREATE UNIQUE INDEX [IX_Categories_Slug] ON [Categories]([Slug]);
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_Categories_ParentCategoryId' AND [object_id] = OBJECT_ID('Categories'))
        BEGIN
            CREATE INDEX [IX_Categories_ParentCategoryId] ON [Categories]([ParentCategoryId]);
        END

        IF COL_LENGTH('Categories', 'ParentCategoryId') IS NOT NULL
        BEGIN
            UPDATE c
            SET c.[ParentCategoryId] = NULL
            FROM [Categories] c
            LEFT JOIN [Categories] p ON p.[Id] = c.[ParentCategoryId]
            WHERE c.[ParentCategoryId] IS NOT NULL
              AND (p.[Id] IS NULL OR c.[ParentCategoryId] = c.[Id]);
        END

        DECLARE @fkName sysname;
        DECLARE @dropSql nvarchar(max);

        SELECT TOP 1 @fkName = fk.[name]
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
        INNER JOIN sys.columns c ON c.[object_id] = fkc.[parent_object_id] AND c.[column_id] = fkc.[parent_column_id]
        WHERE fk.[parent_object_id] = OBJECT_ID('Categories')
          AND c.[name] = 'ParentCategoryId'
          AND fk.[delete_referential_action] <> 0;

        WHILE @fkName IS NOT NULL
        BEGIN
            SET @dropSql = N'ALTER TABLE [Categories] DROP CONSTRAINT [' + REPLACE(@fkName, N']', N']]') + N']';
            EXEC(@dropSql);

            SET @fkName = NULL;
            SELECT TOP 1 @fkName = fk.[name]
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
            INNER JOIN sys.columns c ON c.[object_id] = fkc.[parent_object_id] AND c.[column_id] = fkc.[parent_column_id]
            WHERE fk.[parent_object_id] = OBJECT_ID('Categories')
              AND c.[name] = 'ParentCategoryId'
              AND fk.[delete_referential_action] <> 0;
        END

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fkc.[constraint_object_id] = fk.[object_id]
            INNER JOIN sys.columns c ON c.[object_id] = fkc.[parent_object_id] AND c.[column_id] = fkc.[parent_column_id]
            WHERE fk.[parent_object_id] = OBJECT_ID('Categories')
              AND c.[name] = 'ParentCategoryId'
        )
        BEGIN
            ALTER TABLE [Categories]
            ADD CONSTRAINT [FK_Categories_Categories_ParentCategoryId]
            FOREIGN KEY ([ParentCategoryId]) REFERENCES [Categories]([Id]);
        END

        IF COL_LENGTH('BlogPosts', 'CategorySlug') IS NULL
        BEGIN
            ALTER TABLE [BlogPosts]
            ADD [CategorySlug] nvarchar(80) NOT NULL
            CONSTRAINT [DF_BlogPosts_CategorySlug] DEFAULT N'yasam' WITH VALUES;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = 'IX_BlogPosts_CategorySlug' AND [object_id] = OBJECT_ID('BlogPosts'))
        BEGIN
            EXEC(N'CREATE INDEX [IX_BlogPosts_CategorySlug] ON [BlogPosts]([CategorySlug]);');
        END

        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Slug] = N'teknoloji')
            INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'Teknoloji', N'teknoloji', GETDATE());
        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Slug] = N'yazilim')
            INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'Yazilim', N'yazilim', GETDATE());
        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Slug] = N'tasarim')
            INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'Tasarim', N'tasarim', GETDATE());
        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Slug] = N'yasam')
            INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'Yasam', N'yasam', GETDATE());
        IF NOT EXISTS (SELECT 1 FROM [Categories] WHERE [Slug] = N'is')
            INSERT INTO [Categories] ([Name], [Slug], [CreatedAt]) VALUES (N'Kariyer/Is', N'is', GETDATE());

        IF COL_LENGTH('BlogPosts', 'CategorySlug') IS NOT NULL
        BEGIN
            EXEC(N'
                UPDATE [BlogPosts]
                SET [CategorySlug] = CASE
                    WHEN LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%teknoloji%'' OR LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%tech%'' THEN N''teknoloji''
                    WHEN LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%yazilim%'' OR LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%kod%'' THEN N''yazilim''
                    WHEN LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%tasarim%'' OR LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%ui%'' THEN N''tasarim''
                    WHEN LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%kariyer%'' OR LOWER(ISNULL([Title], N'''') + N'' '' + ISNULL([Content], N'''')) LIKE N''%is%'' THEN N''is''
                    ELSE N''yasam''
                END
                WHERE [CategorySlug] IS NULL OR LTRIM(RTRIM([CategorySlug])) = N'''';
            '); 

            EXEC(N'
                UPDATE p
                SET p.[CategorySlug] = N''yasam''
                FROM [BlogPosts] p
                LEFT JOIN [Categories] c ON c.[Slug] = p.[CategorySlug]
                WHERE c.[Id] IS NULL;
            ');
        END
        ");

            SeedDemoScenicPosts(db, logger);
        }
        catch (SqlException ex)
        {
            logger.LogCritical(ex, "Veritabani baglantisi veya migration adimi basarisiz oldu.");
            throw new InvalidOperationException("Veritabani baslatma adimi basarisiz oldu. Connection string ve SQL erisim izinlerini kontrol et.", ex);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Uygulama baslangicinda veritabani kurulum adimi basarisiz oldu.");
            throw;
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.Use(async (context, next) =>
{
    if (context.Session.GetInt32("UserId") == null)
    {
        var id = context.Request.Cookies["remember_user_id"];
        if (int.TryParse(id, out var userId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var rememberedUser = db.Users
                .AsNoTracking()
                .FirstOrDefault(x => x.Id == userId);

            if (rememberedUser != null)
            {
                context.Session.SetInt32("UserId", rememberedUser.Id);
                context.Session.SetString("UserName", rememberedUser.UserName);
                context.Session.SetString("UserRole", string.IsNullOrWhiteSpace(rememberedUser.Role) ? "User" : rememberedUser.Role);
            }
            else
            {
                context.Response.Cookies.Delete("remember_user_id");
                context.Response.Cookies.Delete("remember_user_name");
                context.Response.Cookies.Delete("remember_user_role");
            }
        }
    }

    await next();
});
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Blog}/{action=Index}/{id?}");
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

app.Run();

static void SeedDemoScenicPosts(AppDbContext db, ILogger logger)
{
    const int targetVisiblePostCount = 9;
    var currentPostCount = db.BlogPosts.Count();
    if (currentPostCount >= targetVisiblePostCount)
        return;

    var owner = db.Users
        .AsNoTracking()
        .ToList()
        .OrderByDescending(x => string.Equals(x.UserName, "SAMET", StringComparison.OrdinalIgnoreCase))
        .ThenBy(x => x.Id)
        .FirstOrDefault();

    if (owner == null)
    {
        logger.LogInformation("Demo gonderi seed atlandi: sistemde kullanici bulunamadi.");
        return;
    }

    var existingTitles = db.BlogPosts
        .Select(x => x.Title)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var demoPosts = new[]
    {
        new
        {
            Title = "Dag Ruzgari ve Sessiz Vadi",
            Content = "Sabahin ilk isiginda vadinin ustune inen sis, dag yolculugunun en guzel aniydi. Sessizlik, kus sesleri ve temiz hava bir araya gelince insanin tum zihni yenileniyor.",
            ImageUrl = "https://images.unsplash.com/photo-1506744038136-46273834b3fb?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Gol Kenarinda Bir Gun Batimi",
            Content = "Gun batimi sirasinda gol yuzeyindeki yansimalar her dakikada renk degistirdi. Turuncudan mora gecen gokyuzu, fotograflardan daha guzel bir manzara sundu.",
            ImageUrl = "https://images.unsplash.com/photo-1470770841072-f978cf4d019e?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Orman Yolu ve Serin Hava",
            Content = "Yagmurdan sonra orman yolunda yururken toprak kokusu tum ortami sarmisti. Agaclarin arasindan sizan isikla birlikte ortam hem huzurlu hem de cok canliydi.",
            ImageUrl = "https://images.unsplash.com/photo-1448375240586-882707db888b?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Karli Zirveler Arasinda Yolculuk",
            Content = "Yuksek rakimda hava daha sert ama manzara cok daha etkileyici. Karla kapli zirveler ve derin vadiler, uzun suren bir yolculugu bile keyifli hale getiriyor.",
            ImageUrl = "https://images.unsplash.com/photo-1464822759023-fed622ff2c3b?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Yesil Tepeler ve Bulut Denizleri",
            Content = "Tepelerin ustunde bulutlarin hareketini izlemek adeta canli bir tablo seyretmek gibi. Ruzgarin ritmine gore degisen goruntu her an baska bir hikaye anlatiyor.",
            ImageUrl = "https://images.unsplash.com/photo-1469474968028-56623f02e42e?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Sahil Yolunda Geceye Dogru",
            Content = "Aksam saatlerinde deniz kiyisinda ilerlerken hem dalga sesi hem de ufuktaki isiklar insana harika bir sakinlik veriyor. Uzun yuruyusler icin en guzel saatler bunlar.",
            ImageUrl = "https://images.unsplash.com/photo-1507525428034-b723cf961d3e?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        },
        new
        {
            Title = "Cam Orman ve Sakin Patika",
            Content = "Yuksek cam agaclarin arasinda uzanan patikada her adim daha derin bir sessizlige goturuyor. Sehir temposundan uzaklasmak icin ideal bir rota.",
            ImageUrl = "https://images.unsplash.com/photo-1501785888041-af3ef285b470?auto=format&fit=crop&w=1600&q=80",
            CategorySlug = "yasam"
        }
    };

    var now = DateTime.Now;
    var inserted = 0;
    foreach (var (post, index) in demoPosts.Select((x, i) => (x, i)))
    {
        if (existingTitles.Contains(post.Title))
            continue;

        db.BlogPosts.Add(new BlogPost
        {
            Title = post.Title,
            Content = post.Content,
            ImageUrl = post.ImageUrl,
            CategorySlug = post.CategorySlug,
            ImagePlacement = "Top",
            ImageWidthPercent = 100,
            UserId = owner.Id,
            CreatedAt = now.AddMinutes(-(demoPosts.Length - index) * 7)
        });
        inserted++;
    }

    if (inserted <= 0)
        return;

    db.SaveChanges();
    logger.LogInformation("Demo manzara gonderileri eklendi. Eklenen sayi: {Count}", inserted);
}
