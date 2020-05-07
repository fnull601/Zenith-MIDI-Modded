﻿using IWshRuntimeLibrary;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using File = System.IO.File;

namespace ZenithShared
{
    public static class ZenithUpdates
    {
        public static readonly string DefaultUpdatePackagePath = "Updates\\pkg.zip";
        public static readonly string DataAssetName32 = "Zenithx86.zip";
        public static readonly string DataAssetName64 = "Zenithx64.zip";
        public static readonly string InstallerPath = "Updates\\ins.exe";
        public static readonly string SettingsPath = "Settings\\meta.kvs";
        public static readonly string ProgramName = "Zenith";
        public static readonly string ExeName = "Zenith.exe";
        public static readonly string[] ProcessNames = new[] { "Zenith", "Zenith-MIDI" };
        public static readonly string InstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith");

        public static readonly string InstallerVer = "4";

        public static readonly string ApiURL = "https://api.github.com/repos/arduano/Zenith-MIDI/releases/latest";

        public static dynamic GetHTTPJSON(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UserAgent = ProgramName;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        return JsonConvert.DeserializeObject(reader.ReadToEnd());
                    }
                }
            }
        }

        public static Stream GetHTTPData(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UserAgent = ProgramName;

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream data = new MemoryStream();
                    stream.CopyTo(data);
                    data.Position = 0;
                    return data;
                }
            }
        }

        public static Stream GetHTTPDataProgressive(string uri, Action<long, long> progressCallback)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.UserAgent = ProgramName;

            var res = request.BeginGetResponse((o) => { }, "");

            using (HttpWebResponse response = (HttpWebResponse)request.EndGetResponse(res))
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream data = new MemoryStream();
                    var buffer = new byte[2048];
                    var copied = 0;
                    while (copied < response.ContentLength)
                    {
                        var r = stream.Read(buffer, 0, buffer.Length);
                        data.Write(buffer, 0, r);
                        copied += r;
                        progressCallback(copied, response.ContentLength);
                    }
                    data.Position = 0;
                    return data;
                }
            }
        }

        public static string GetLatestVersion() => GetLatestVersion(ApiURL);
        public static string GetLatestVersion(string repo)
        {
            var data = GetHTTPJSON(repo);
            return (string)data.tag_name;
        }

        public static Stream DownloadAssetData(string filename) => DownloadAssetData(filename, ApiURL);
        public static Stream DownloadAssetData(string filename, string repo)
        {
            var data = GetHTTPJSON(repo);
            var assets = (JArray)data.assets;
            var asset = (dynamic)assets.Where(a => ((dynamic)a).name == filename).First();
            var url = (string)asset.browser_download_url;
            return GetHTTPData(url);
        }

        public static Stream DownloadAssetDataProgressive(string filename, Action<long, long> progressCallback) => DownloadAssetDataProgressive(filename, ApiURL, progressCallback);
        public static Stream DownloadAssetDataProgressive(string filename, string repo, Action<long, long> progressCallback)
        {
            var data = GetHTTPJSON(repo);
            var assets = (JArray)data.assets;
            var asset = (dynamic)assets.Where(a => ((dynamic)a).name == filename).First();
            var url = (string)asset.browser_download_url;
            return GetHTTPDataProgressive(url, progressCallback);
        }

        public static bool IsAnotherProcessRunning()
        {
            var kivas = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetEntryAssembly().Location)).ToArray();
            if (kivas.Length > 1) return true;
            return false;
        }

        public static void KillAllProcesses()
        {
            var kivas = Process.GetProcesses().Where(p => ProcessNames.Contains(p.ProcessName));
            var current = Process.GetCurrentProcess();
            foreach (var k in kivas)
            {
                if (k.Id == current.Id) continue;
                if (!k.HasExited) k.Kill();
                try
                {
                    k.WaitForExit(10000);
                }
                catch { continue; }
                if (!k.HasExited) throw new InstallFailedException("Could not kill process \"" + k.ProcessName + "\" with pid " + k.Id);
            }
        }

        public static void InstallFromStream(Stream s)
        {
            var basePath = InstallPath;
            using (ZipArchive archive = new ZipArchive(s))
            {
                foreach (var e in archive.Entries)
                {
                    if (e.FullName.EndsWith("\\") || e.FullName.EndsWith("/")) continue;
                    if (!Directory.Exists(Path.Combine(basePath, Path.GetDirectoryName(e.FullName))))
                        Directory.CreateDirectory(Path.Combine(basePath, Path.GetDirectoryName(e.FullName)));

                    var fs = File.Open(Path.Combine(basePath, e.FullName), FileMode.OpenOrCreate);
                    e.Open().CopyTo(fs);
                    fs.Close();
                }
            }
        }

        public static void WriteVersionSettings(string version, bool autoUpdate = true, bool installed = true)
        {
            var path = Path.Combine(InstallPath, SettingsPath);
            if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));

            JObject jobj;
            if (File.Exists(path))
            {
                var s = new StreamReader(new GZipStream(File.Open(path, FileMode.Open), CompressionMode.Decompress));
                var text = s.ReadToEnd();
                jobj = (JObject)JsonConvert.DeserializeObject(text);
                s.Close();
            }
            else
            {
                jobj = new JObject();
            }

            if (jobj.ContainsKey("version")) jobj.Remove("version");
            if (jobj.ContainsKey("autoUpdate")) jobj.Remove("autoUpdate");
            if (jobj.ContainsKey("installed")) jobj.Remove("installed");
            if (jobj.ContainsKey("installerVer")) jobj.Remove("installerVer");
            jobj.Add("version", version);
            jobj.Add("autoUpdate", autoUpdate);
            jobj.Add("installed", installed);
            jobj.Add("installerVer", InstallerVer);

            var stream = new StreamWriter(new GZipStream(File.Open(path, FileMode.Create), CompressionMode.Compress));
            stream.Write(JsonConvert.SerializeObject(jobj));
            stream.Close();
        }

        public static void CopySelfInside(string path)
        {
            var p = Path.Combine(InstallPath, path);
            if (!Directory.Exists(Path.GetDirectoryName(p))) Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.Copy(System.Reflection.Assembly.GetEntryAssembly().Location, p, true);
        }

        public static void UpdateInstaller()
        {
            var data = DownloadAssetData("ZenithInstaller.exe");
            var s = File.Open(InstallerPath, FileMode.Create);
            data.Position = 0;
            data.CopyTo(s);
            s.Close();
        }

        public static void CreateStartShortcut()
        {
            string shortcutLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs\\" + ProgramName + ".lnk");
            if (File.Exists(shortcutLocation)) File.Delete(shortcutLocation);
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutLocation);

            shortcut.Description = "Kiva";
            shortcut.TargetPath = Path.Combine(InstallPath, ExeName);
            shortcut.WorkingDirectory = Path.GetDirectoryName(shortcut.TargetPath);
            shortcut.Save();
        }

        public static void DeleteStartShortcut()
        {
            string shortcutLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs\\" + ProgramName + ".lnk");
            if (File.Exists(shortcutLocation)) File.Delete(shortcutLocation);
        }

        public static void CreateDesktopShortcut()
        {
            string shortcutLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ProgramName + ".lnk");
            if (File.Exists(shortcutLocation)) File.Delete(shortcutLocation);
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutLocation);

            shortcut.Description = "Kiva";
            shortcut.TargetPath = Path.Combine(InstallPath, ExeName);
            shortcut.WorkingDirectory = Path.GetDirectoryName(shortcut.TargetPath);
            shortcut.Save();
        }

        public static void DeleteDesktopShortcut()
        {
            string shortcutLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), ProgramName + ".lnk");
            if (File.Exists(shortcutLocation)) File.Delete(shortcutLocation);
        }

        public static void DeleteProgramFolder()
        {
            Directory.Delete(InstallPath, true);
        }

        public static void CreateUninstallScript()
        {
            File.WriteAllText(
                Path.Combine(InstallPath, "uninstall.bat"),
                @"
cd ..
copy {ins} %temp%\{tmp}
%temp%\{tmp} uninstall
del %temp%\{tmp}
"
            .Replace("{ins}", Path.Combine(InstallPath, InstallerPath))
            .Replace("{tmp}", ProgramName + "Ins.exe")
            );
        }
    }
}
