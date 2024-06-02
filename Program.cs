using Microsoft.Data.Sqlite;
using Dapper;
using System.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.ServiceModel.Syndication;
using System.Xml;

var builder = WebApplication.CreateBuilder(args);

var connectionString = "Data Source=./wwwroot/database.db;";
builder.Services.AddSingleton<IDbConnection>(_ => new SqliteConnection(connectionString));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RSSCookie";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
       // options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.None; 
    });
builder.Services.AddAuthorization(); 
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRouting();


using (var connection = new SqliteConnection(connectionString))
{
    connection.Open();
    connection.Execute(@"CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        email TEXT NOT NULL UNIQUE,
                        password TEXT NOT NULL
                      
                    )");
    
    connection.Execute(@"CREATE TABLE IF NOT EXISTS Feeds (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        Url TEXT NOT NULL,
                        FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                    )");

}
app.UseStaticFiles();

app.MapGet("/", async context =>
{
    if (context.User.Identity.IsAuthenticated)
    {
        Console.WriteLine("authhh");
        
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Console.WriteLine($"{userId}");
      
        var feedsHtml = $@"
          <!doctype html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>feedify</title>
    <link rel=""icon"" type=""image/svg"" href=""rss-svgrepo-com.svg"">
    <link href=""style.css"" rel=""stylesheet"">

    <link href=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"" rel=""stylesheet"" integrity=""sha384-QWTKZyjpPEjISv5WaRU9OFeRpok6YctnYmDr5pNlyT2bRjXh0JMhjY6hW+ALEwIH"" crossorigin=""anonymous"">
    
</head>
<body>
<button type=""button"" class=""btn btn-danger btn-block mt-2"" hx-post =""/logout"" >Log out</button>

 <div class=""feeds-section mt-5"">
      
        <div class=""text-center mt-4"">
       <form id=""addFeedForm"">
  <input type=""hidden"" name=""userId"" value=""{userId}"">
                <div class=""form-group"">
                    <label for=""feedUrl"" class=""mb-1"">Feed URL</label>
                <div class=""row justify-content-center"">
    <div class=""col-4"">
        <input type=""url"" class=""form-control mb-2"" id=""feedUrl"" placeholder=""Enter RSS/ATOM feed URL"" name=""url"" required>
    </div>
</div>
                </div>
                <button type=""button"" class=""btn btn-danger btn-block mt-2"" hx-post=""/add-feed"" hx-target=""#added-container"" hx-swap=""outerHTML"">Add Feed</button>
            </form>
<button type=""button"" class=""btn btn-danger btn-block mt-2"" hx-get=""/get-feeds/{userId}"" hx-target=""#feeds-container"">Get Feeds</button>
<div id=""added-container"">

</div>
<div id=""feeds-container"">


</div>
        </div>

    </div>
    <script src=""https://unpkg.com/htmx.org@1.9.2""></script>
    <script src=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"" integrity=""sha384-YvpcrYf0tY3lHB60NNkmXc5s9fDVZLESaAA55NDzOxhy9GkcIdslK1eN7N6jIeHz"" crossorigin=""anonymous""></script>       
      </body>
</html>
"; 
        await context.Response.WriteAsync(feedsHtml);

    }
    else
    {
        Console.WriteLine("not");
        context.Response.Headers["Content-Type"] = "text/html";
        await context.Response.SendFileAsync("wwwroot/index.html");
    }
  
});

app.MapPost("/get-user", async (IDbConnection db, HttpContext context) =>
{
    Console.WriteLine(context.User.Identity.IsAuthenticated);
    var email = context.Request.Form["email2"].ToString().Trim();
    var password = context.Request.Form["password2"].ToString();
    var user = await db.QuerySingleOrDefaultAsync<dynamic>(
        "SELECT Id FROM Users WHERE email = @Email AND password = @Password",
        new { Email = email, Password = password }
    );

    if (user != null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        };

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

        context.Response.Headers["HX-Redirect"] = "/";

    }
    else
    {
     
        var loginFailedHtml = @"
          
<div class='col-md-2 error-login z text-danger d-block'>
                <h6>Incorrect email or password. Please try again.</h6>
           
 </div>";

        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(loginFailedHtml);

 
    }
});

app.MapPost("/add-user", async (HttpContext context, IDbConnection db) =>
{
    var email = context.Request.Form["email"].ToString().Trim();
    var password = context.Request.Form["password"].ToString();
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
    {
        return Results.Content("<div class='text-danger'>Fields cannot be empty</div>", "text/html");
    }

    try
    {
        var resultId = await db.QuerySingleOrDefaultAsync<int>("INSERT INTO Users (email, password) VALUES (@Email, @Password); SELECT last_insert_rowid();", new { Email = email, Password = password });
        var successHtml = @"
            <div class='success-message'>
                <p>Your registration was successful!</p>
</div>
              
            ";
        return Results.Content(successHtml, "text/html");
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
    {
        var errorHtml = @"
 <div class='err-message'>
            <div class='text-danger mt-2'>
                <h6>Email already exists! Please use a different email.</h6>
            </div>
</div>
 <form id='regForm' hx-post='/add-user' hx-target='#regModal .modal-body' hx-swap='innerHTML'>
                    <div class='form-group'>
                        <label for='email' class='mb-1'>Email</label>
                        <input type='email' class='form-control mb-2' id='email' placeholder='Enter email' name='email' required>
                    </div>
                    <div class='form-group'>
                        <label for='password' class='mb-1'>Password</label>
                        <input type='password' class='form-control mb-2' id='password' placeholder='Enter password' name='password' required>
                    </div>
                    <div class='row justify-content-center' id='errormsg'>
                        <div class='col-md-6'>
                            <button type='submit' class='btn btn-danger btn-block mt-2' id='rsumbit'>Register</button>
                        </div>
                    </div>
                </form>

";

        return Results.Content(errorHtml, "text/html");
    }
});


// Add an RSS/ATOM feed
app.MapPost("/add-feed", async (HttpContext context, IDbConnection db) =>
{
    Console.WriteLine("inside add feed");
    Console.WriteLine(context.Request.Form["userId"]);
    var userId = int.Parse(context.Request.Form["userId"]);
    Console.WriteLine("user id inside add feed",userId);
    var url = context.Request.Form["url"].ToString().Trim();
    if (url.Length != 0)
    {
        
            var resultId = await db.QuerySingleOrDefaultAsync<int>("INSERT INTO Feeds (UserId, Url) VALUES (@UserId, @Url); SELECT last_insert_rowid();", new { UserId = userId, Url = url });

            var successHtml = "<div class='text-success'>Feed added successfully!</div>";
            context.Response.Headers["HX-Redirect"] = "/";
            return Results.Content(successHtml, "text/html");

    }
    else
    {
        var successHtml = "<div class='text-success'>Please enter url!</div>";
       // context.Response.Headers["HX-Redirect"] = "/";
        return Results.Content(successHtml, "text/html");
    }

});


// Endpoint to get all feeds for a user
app.MapGet("/get-feeds/{userId}", async (HttpContext context, IDbConnection db) =>
{
    var userId = context.Request.RouteValues["userId"];
    var feeds = await db.QueryAsync<dynamic>("SELECT Id, Url FROM Feeds WHERE UserId = @UserId", new { UserId = userId });
    var feedsHtml = @"
        <h2 class=""text-center"">Your RSS Feeds</h2>
        <div id=""feeds-list"" class=""text-center"">
    ";
   
    foreach (var feed in feeds)
    {
        feedsHtml += $@"
            <div><span>{feed.Url}</span>
<button class=""btn btn-sm btn-outline-danger delete-feed-btn"" hx-post=""/delete-feed/{userId}/{feed.Id}"" hx-target="".feeds-section"" hx-swap=""outerHTML"">
    <span aria-hidden=""true"">&times;</span>
</button>
<button class=""btn btn-sm btn-outline-primary fetch-news-btn"" hx-get=""/fetch-news/{feed.Id}"" hx-target=""#news-container"" hx-swap=""innerHTML"">Fetch News</button>
</div>
        ";
    }

    feedsHtml += @"
        </div>
   <div id=""news-container"" class=""mt-4""></div>
    ";

    return Results.Content(feedsHtml, "text/html");
});


app.MapPost("/delete-feed/{userId}/{feedId}", async (HttpContext context, IDbConnection db) =>
{
    var userId = context.Request.RouteValues["userId"];
    var feedId = context.Request.RouteValues["feedId"];
    Console.WriteLine(feedId);
    try
    {
        await db.ExecuteAsync("DELETE FROM Feeds WHERE Id = @FeedId AND UserId = @UserId", new { FeedId = feedId, UserId = userId });

       
        context.Response.Headers["HX-Redirect"] = "/";
        
    }
    catch (Exception ex)
    {
        var errorHtml = $"<div class='text-danger'>Error: {ex.Message}</div>";
      
    }
});
// Endpoint to fetch the latest news from a feed
app.MapGet("/fetch-news/{feedId}", async (HttpContext context, IDbConnection db) =>
{
    var feedId = context.Request.RouteValues["feedId"];
    var feed = await db.QuerySingleOrDefaultAsync<dynamic>("SELECT Url FROM Feeds WHERE Id = @FeedId", new { FeedId = feedId });
    if (feed == null)
    {
        return Results.Content("<div class='text-danger'>Feed not found!</div>", "text/html");
    }

    var feedUrl = feed.Url;
    var newsHtml = $"<h2 class=\"text-center\">Latest News for Feed: {feedUrl}</h2><div class=\"news-list\">";

    try
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse
        };

        using var reader = XmlReader.Create(feedUrl, settings);
        var syndicationFeed = SyndicationFeed.Load(reader);
        
        if (syndicationFeed != null)
        {
            
            foreach (var item in syndicationFeed.Items)
            {
              
                newsHtml += $@"
                    <div class='news-item'>
                      
                        <p>{item.Summary?.Text ?? "No summary available"}</p>
                        <a href='{item.Links[0].Uri}' target='_blank'>Read more</a>
                    </div>";
            }
        }
    }
    catch (Exception ex)
    {
        newsHtml += $"<div class='text-danger'>Error fetching news</div>";
    }


    newsHtml += "</div>";
    return Results.Content(newsHtml, "text/html");
});




// Endpoint to logout
app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    context.Response.Headers["HX-Redirect"] = "/";
});







app.Run();
public class user
{
    public long Id { get; set; }
    public string? email { get; set; }
    public string? password { get; set; }
}