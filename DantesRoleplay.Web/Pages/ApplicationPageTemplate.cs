using System.Net;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Web.Pages;

/// <summary>Safe read-only landing page for a registered application without authored page content.</summary>
public static class ApplicationPageTemplate
{
    public static string Render(ApplicationRegistration application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var id = WebUtility.HtmlEncode(application.Id.Value);
        var name = WebUtility.HtmlEncode(application.DisplayName);
        var description = WebUtility.HtmlEncode(application.Description);
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{name}}</title>
              <script type="module" src="/components/system-workspace.js"></script>
              <style>
                body { background: #101411; color: #edf7e8; font: 16px/1.5 system-ui, sans-serif; margin: 0; }
                system-navigation { display: block; margin: 1rem auto; max-width: 68rem; padding: 0 1rem; }
                main { background: #172019; border: 1px solid #476647; border-radius: 1rem; margin: 3rem auto; max-width: 48rem; padding: 2rem; }
                h1 { margin-top: 0; }
                p { color: #c6d7c2; }
              </style>
            </head>
            <body>
              <system-navigation application-id="{{id}}"></system-navigation>
              <main>
                <p>Application page</p>
                <h1>{{name}}</h1>
                <p>{{description}}</p>
                <p>This application has its own direct page. Add an authored page when its game interface is ready.</p>
              </main>
            </body>
            </html>
            """;
    }
}
