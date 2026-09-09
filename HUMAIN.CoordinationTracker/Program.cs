using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HUMAIN.CoordinationTracker
{
    internal class Program
    {
        private const string ClientId = "cBv3FpuI3rKRAfhYpwSCDsVZTzu8daWXHZduYz4Q0mfttDxo";
        private const string RedirectUri = "http://localhost:8080/";

        private static readonly string[] Scopes =
        {
            "data:read",
            "account:read"
        };

        static void Main(string[] args)
        {
            try
            {
                RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("ERROR:");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine(ex);
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task RunAsync()
        {
            Console.WriteLine("HUMAIN Coordination Tracker");
            Console.WriteLine("===========================");
            Console.WriteLine();

            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);
            string state = Guid.NewGuid().ToString("N");

            string scope = string.Join(" ", Scopes);

            string authorizationUrl =
                "https://developer.api.autodesk.com/authentication/v2/authorize" +
                "?response_type=code" +
                "&client_id=" + Uri.EscapeDataString(ClientId) +
                "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                "&scope=" + Uri.EscapeDataString(scope) +
                "&state=" + Uri.EscapeDataString(state) +
                "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
                "&code_challenge_method=S256";

            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                Console.WriteLine("Opening Autodesk login...");
                Console.WriteLine();

                Process.Start(new ProcessStartInfo
                {
                    FileName = authorizationUrl,
                    UseShellExecute = true
                });

                Console.WriteLine("Waiting for Autodesk authorization...");

                HttpListenerContext context =
                    await listener.GetContextAsync();

                string returnedState =
                    context.Request.QueryString["state"];

                string code =
                    context.Request.QueryString["code"];

                string error =
                    context.Request.QueryString["error"];

                if (!string.IsNullOrWhiteSpace(error))
                {
                    await SendBrowserResponse(
                        context,
                        "<html><body style='font-family:Segoe UI'>" +
                        "<h2>Autodesk login failed.</h2>" +
                        "<p>You can close this browser window.</p>" +
                        "</body></html>");

                    throw new Exception(
                        "Autodesk authorization error: " + error);
                }

                if (returnedState != state)
                {
                    await SendBrowserResponse(
                        context,
                        "<html><body style='font-family:Segoe UI'>" +
                        "<h2>Security validation failed.</h2>" +
                        "<p>You can close this browser window.</p>" +
                        "</body></html>");

                    throw new Exception(
                        "OAuth state validation failed.");
                }

                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new Exception(
                        "No authorization code was returned.");
                }

                await SendBrowserResponse(
                    context,
                    "<html><body style='font-family:Segoe UI'>" +
                    "<h2>Autodesk login successful.</h2>" +
                    "<p>You can close this window and return to HUMAIN Coordination Tracker.</p>" +
                    "</body></html>");

                listener.Stop();

                Console.WriteLine("Authorization code received.");
                Console.WriteLine("Requesting access token...");

                TokenResponse token =
                    await ExchangeCodeForToken(
                        code,
                        codeVerifier);

                Console.ForegroundColor =
                    ConsoleColor.Green;

                Console.WriteLine();
                Console.WriteLine(
                    "================================");

                Console.WriteLine(
                    "AUTODESK LOGIN SUCCESSFUL");

                Console.WriteLine(
                    "================================");

                Console.ResetColor();

                Console.WriteLine(
                    "Token Type : " +
                    token.token_type);

                Console.WriteLine(
                    "Expires In : " +
                    token.expires_in +
                    " seconds");

                Console.WriteLine();
                Console.WriteLine(
                    "Access token received successfully.");

                Console.WriteLine(
                    "The token itself is intentionally NOT displayed.");

                Console.WriteLine();
                Console.WriteLine(
                    "Reading Autodesk hubs...");

                Console.WriteLine();

                await GetHubsAndProjects(
                    token.access_token);
            }
        }

        private static async Task<TokenResponse>
            ExchangeCodeForToken(
                string authorizationCode,
                string codeVerifier)
        {
            using (HttpClient client =
                new HttpClient())
            {
                Dictionary<string, string> values =
                    new Dictionary<string, string>
                    {
                        {
                            "grant_type",
                            "authorization_code"
                        },
                        {
                            "client_id",
                            ClientId
                        },
                        {
                            "code",
                            authorizationCode
                        },
                        {
                            "redirect_uri",
                            RedirectUri
                        },
                        {
                            "code_verifier",
                            codeVerifier
                        }
                    };

                using (FormUrlEncodedContent content =
                    new FormUrlEncodedContent(values))
                {
                    HttpResponseMessage response =
                        await client.PostAsync(
                            "https://developer.api.autodesk.com/authentication/v2/token",
                            content);

                    string responseText =
                        await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception(
                            "Token request failed." +
                            Environment.NewLine +
                            "HTTP " +
                            (int)response.StatusCode +
                            Environment.NewLine +
                            responseText);
                    }

                    JavaScriptSerializer serializer =
                        new JavaScriptSerializer();

                    TokenResponse token =
                        serializer.Deserialize<TokenResponse>(
                            responseText);

                    if (token == null ||
                        string.IsNullOrWhiteSpace(
                            token.access_token))
                    {
                        throw new Exception(
                            "Access token was not returned.");
                    }

                    return token;
                }
            }
        }

        private static async Task
            GetHubsAndProjects(
                string accessToken)
        {
            using (HttpClient client =
                new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        accessToken);

                client.DefaultRequestHeaders.Add(
                    "User-Agent",
                    "HUMAIN-Coordination-Tracker");

                HttpResponseMessage response =
                    await client.GetAsync(
                        "https://developer.api.autodesk.com/project/v1/hubs");

                string json =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        "Failed to read Autodesk hubs." +
                        Environment.NewLine +
                        "HTTP " +
                        (int)response.StatusCode +
                        Environment.NewLine +
                        json);
                }

                JavaScriptSerializer serializer =
                    new JavaScriptSerializer();

                HubsResponse hubs =
                    serializer.Deserialize<HubsResponse>(
                        json);

                if (hubs == null ||
                    hubs.data == null ||
                    hubs.data.Count == 0)
                {
                    Console.WriteLine(
                        "No hubs were returned.");

                    return;
                }

                Console.ForegroundColor =
                    ConsoleColor.Cyan;

                Console.WriteLine(
                    "AUTODESK HUBS");

                Console.WriteLine(
                    "==============");

                Console.ResetColor();

                foreach (HubData hub in hubs.data)
                {
                    Console.WriteLine();

                    Console.WriteLine(
                        "Hub Name : " +
                        GetHubName(hub));

                    Console.WriteLine(
                        "Hub ID   : " +
                        hub.id);

                    await GetProjects(
                        client,
                        hub.id);
                }
            }
        }

        private static async Task
            GetProjects(
                HttpClient client,
                string hubId)
        {
            string url =
                "https://developer.api.autodesk.com/project/v1/hubs/" +
                Uri.EscapeDataString(hubId) +
                "/projects";

            HttpResponseMessage response =
                await client.GetAsync(url);

            string json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.ForegroundColor =
                    ConsoleColor.Yellow;

                Console.WriteLine(
                    "Could not read projects from this hub.");

                Console.WriteLine(
                    "HTTP " +
                    (int)response.StatusCode);

                Console.WriteLine(json);

                Console.ResetColor();

                return;
            }

            JavaScriptSerializer serializer =
                new JavaScriptSerializer();

            ProjectsResponse projects =
                serializer.Deserialize<ProjectsResponse>(
                    json);

            if (projects == null ||
                projects.data == null ||
                projects.data.Count == 0)
            {
                Console.WriteLine(
                    "  No projects returned.");

                return;
            }

            foreach (ProjectData project
                in projects.data)
            {
                string projectName =
                    GetProjectName(project);

                if (projectName.IndexOf(
                    "HUMAIN",
                    StringComparison.OrdinalIgnoreCase)
                    >= 0)
                {
                    Console.WriteLine();

                    Console.ForegroundColor =
                        ConsoleColor.Green;

                    Console.WriteLine(
                        ">>> HUMAIN PROJECT FOUND <<<");

                    Console.WriteLine(
                        "Project Name : " +
                        projectName);

                    Console.WriteLine(
                        "Project ID   : " +
                        project.id);

                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(
                        "  Project: " +
                        projectName);
                }
            }
        }

        private static string GetHubName(
            HubData hub)
        {
            if (hub == null)
                return "(Unknown)";

            if (hub.attributes == null)
                return "(Unknown)";

            if (string.IsNullOrWhiteSpace(
                hub.attributes.name))
                return "(Unknown)";

            return hub.attributes.name;
        }

        private static string GetProjectName(
            ProjectData project)
        {
            if (project == null)
                return "(Unknown)";

            if (project.attributes == null)
                return "(Unknown)";

            if (string.IsNullOrWhiteSpace(
                project.attributes.name))
                return "(Unknown)";

            return project.attributes.name;
        }

        private static async Task
            SendBrowserResponse(
                HttpListenerContext context,
                string html)
        {
            byte[] buffer =
                Encoding.UTF8.GetBytes(html);

            context.Response.ContentType =
                "text/html; charset=utf-8";

            context.Response.ContentLength64 =
                buffer.Length;

            await context.Response.OutputStream
                .WriteAsync(
                    buffer,
                    0,
                    buffer.Length);

            context.Response.OutputStream.Close();
        }

        private static string
            GenerateCodeVerifier()
        {
            byte[] bytes =
                new byte[32];

            using (RandomNumberGenerator rng =
                RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Base64UrlEncode(bytes);
        }

        private static string
            GenerateCodeChallenge(
                string verifier)
        {
            byte[] bytes =
                Encoding.ASCII.GetBytes(
                    verifier);

            byte[] hash;

            using (SHA256 sha256 =
                SHA256.Create())
            {
                hash =
                    sha256.ComputeHash(bytes);
            }

            return Base64UrlEncode(hash);
        }

        private static string
            Base64UrlEncode(
                byte[] bytes)
        {
            return Convert
                .ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private class TokenResponse
        {
            public string token_type
            {
                get;
                set;
            }

            public int expires_in
            {
                get;
                set;
            }

            public string access_token
            {
                get;
                set;
            }

            public string refresh_token
            {
                get;
                set;
            }
        }

        private class HubsResponse
        {
            public List<HubData> data
            {
                get;
                set;
            }
        }

        private class HubData
        {
            public string id
            {
                get;
                set;
            }

            public HubAttributes attributes
            {
                get;
                set;
            }
        }

        private class HubAttributes
        {
            public string name
            {
                get;
                set;
            }
        }

        private class ProjectsResponse
        {
            public List<ProjectData> data
            {
                get;
                set;
            }
        }

        private class ProjectData
        {
            public string id
            {
                get;
                set;
            }

            public ProjectAttributes attributes
            {
                get;
                set;
            }
        }

        private class ProjectAttributes
        {
            public string name
            {
                get;
                set;
            }
        }
    }
}