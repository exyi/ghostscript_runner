using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Medallion.Shell;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace ghostscript_runner.Pages
{
    public class AnketaModel : PageModel
    {
        [BindProperty]
        public string Msg { get; set; } = "";
        public void OnPostYes()
        {
            var x = (Msg ?? "").Replace("\n", "");
            System.IO.File.AppendAllText("anketa-yes.log", $"{DateTime.Now.ToString("o")} {HttpContext.Connection.RemoteIpAddress}: YES {x}\n");
        }
        public void OnPostNo()
        {
            var x = (Msg ?? "").Replace("\n", "");
            System.IO.File.AppendAllText("anketa-no.log", $"{DateTime.Now.ToString("o")} {HttpContext.Connection.RemoteIpAddress}: NO {x}\n");
        }
    }
    public class IndexModel : PageModel
    {
        [BindProperty]
        public string Script { get; set; } = "";

        [BindProperty]
        public int PixelWidth { get; set; } = 640;

        [BindProperty]
        public string Task { get; set; } = "";

        public bool Printed { get; set; } = false;

        public string GsOutput { get; set; } = "";
        public string Image { get; set; } = null;

        private readonly ILogger _logger;
        private readonly string wwwrootFolder;

        public IndexModel(ILogger<IndexModel> logger, Microsoft.Extensions.Hosting.IHostEnvironment env)
        {
            _logger = logger;
            wwwrootFolder = env.ContentRootPath + "/wwwroot";
        }

        public void OnGet()
        {
            _logger.LogInformation("processing GET");
        }

        private Random rand = new Random();

        string GetTaskInput() =>
            Task switch {
                "" => "",
                null => "",
                "square" => rand.NextDouble() * 200 + "",
                "lines" => rand.Next(5, 40) + "",
                "star" => rand.Next(6, 50) + "",
                "ruler" => rand.NextDouble() * 2.5 + 0.5 + "",
                "triangle" => rand.Next(4, 10) + "",
                "chess1" => rand.Next(5, 10) + "",
                "chess2" => rand.Next(5, 10) + "",
                _ => throw new Exception("Neplatná úloha " + Task),
            };

        public async Task<IActionResult> OnPostPreview()
        {
            var id = Guid.NewGuid();

            _logger.LogInformation("processing POST");
            string script = Request.Form["script"];
            if (!ValidateScript(script))
                return Page();

            var psFile = $"{wwwrootFolder}/out/{id}.ps";
            var pngFile = $"{wwwrootFolder}/out/{id}.png";

            var input = GetTaskInput();

            System.IO.File.WriteAllText(psFile, input + "\n" + script + "\n stack\nshowpage");
            var outputLines = new List<string>();

            var dpi = PixelWidth / 8.27;

            var args = new [] {
                "-dNOPAUSE", "-dQUIET", "-dBATCH", "-dSAFER", "-sDEVICE=pnggray", $"-sOutputFile={pngFile}", $"-r{dpi}", "-dTextAlphaBits=4", "-dGraphicsAlphaBits=4", "-sPAPERSIZE=a4", "-dFIXEDMEDIA", psFile };

            _logger.LogInformation("Running gs {args}", string.Join(" ", args));

            var gsCmd = Command.Run("gs", args,
                o => { o.Timeout(TimeSpan.FromSeconds(30)); })
                .RedirectTo(outputLines)
                .RedirectStandardErrorTo(outputLines)
                ;

            var gsOut = await gsCmd.Task;
            this.GsOutput = (gsOut.Success ? "" : $"Status: {gsOut.ExitCode}\n") + string.Join("\n", outputLines);
            if (gsOut.Success)
                this.Image = $"/out/{id}.png";
            _logger.LogInformation("script = {0}", script);
            return Page();
        }

        public bool ValidateScript(string script)
        {
            if (Regex.IsMatch(script, @"\bshowpage\b"))
            {
                this.GsOutput = "Nepoužívejte tady prosím příkaz showpage. Smažte ho, stránka se vytiskne sama.";
                return false;
            }
            return true;
        }

        public async Task<IActionResult> OnPostPrint()
        {
            string script = Request.Form["script"];
            if (!ValidateScript(script))
                return Page();
            var psFile = $"{wwwrootFolder}/out/{Guid.NewGuid()}.ps";
            var input = GetTaskInput();
            var script2 = "%!PS\n" + input + "\n" + script + "\nshowpage\n";
            System.IO.File.WriteAllText(psFile, script2);

            System.IO.File.AppendAllLines("submit.log", new [] {
                $"Submit from {HttpContext.Connection.RemoteIpAddress,20} task {Task,6} input {input} : {psFile}"
            });

            var outputLines = new List<string>();

            var ncCmd = Command.Run("nc", new [] { "192.168.2.101", "9100" })
                .RedirectFrom(new StringReader(script2))
                .RedirectTo(outputLines)
                .RedirectStandardErrorTo(outputLines)
                ;

            await System.Threading.Tasks.Task.WhenAny(
                ncCmd.Task,
                System.Threading.Tasks.Task.Delay(1_000));
            ncCmd.Kill();

            this.GsOutput = string.Join("\n", outputLines);

            Printed = true;

            if (string.IsNullOrWhiteSpace(GsOutput))
                this.GsOutput = "Vstup odeslán, tiskárna neoznámila žádnou chybu";

            return Page();
        }
    }
}
