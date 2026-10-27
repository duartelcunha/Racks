using System;
using System.IO;

namespace Racks.Core
{
    internal static class DesktopIconManager
    {
        public static string RacksWorkspacePath { get; private set; } = string.Empty;

        public static void Initialize()
        {
            try
            {
                RacksWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RacksWorkspace");
                // The icon is a dedicated high-res one for the workspace folder (separate from the app's own
                // tray/window icon), deployed next to the exe by the csproj.
                string iconSource = Path.Combine(AppContext.BaseDirectory, "Icon", "WorkspaceFolder.ico");
                string librariesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Libraries");
                Setup(RacksWorkspacePath, iconSource, librariesPath, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init workspace: {ex.Message}");
            }
        }

        // Everything Initialize does, with the four places it touches passed in so a test can run it against a temp
        // folder (the workspace, the icon to copy, the Libraries folder, the Desktop folder the library points at).
        // Same steps, same order, same swallowing of errors as before.
        internal static void Setup(string workspacePath, string iconSource, string librariesPath, string desktopPath)
        {
            try
            {
                // Create the workspace folder
                if (!Directory.Exists(workspacePath))
                {
                    DirectoryInfo di = Directory.CreateDirectory(workspacePath);
                    di.Attributes |= FileAttributes.Hidden; // Hide the workspace folder itself from the user's home directory so it's not messy
                }

                ApplyWorkspaceIcon(workspacePath, iconSource);
                CreateDesktopLibrary(workspacePath, librariesPath, desktopPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to init workspace: {ex.Message}");
            }
        }

        // Give RacksWorkspace the Racks logo in Explorer so it's instantly recognizable among
        // the user's home folders. Done the standard Windows way: a hidden copy of the icon
        // inside the folder + a desktop.ini pointing at it, with the folder flagged ReadOnly
        // so the shell reads that desktop.ini.
        private static void ApplyWorkspaceIcon(string workspacePath, string srcIcon)
        {
            try
            {
                if (!File.Exists(srcIcon)) return;

                string iconInFolder = Path.Combine(workspacePath, ".RacksIcon.ico");
                if (!File.Exists(iconInFolder)
                    || File.GetLastWriteTimeUtc(srcIcon) > File.GetLastWriteTimeUtc(iconInFolder))
                {
                    // Clear hidden/system so we can overwrite, then copy the real logo in.
                    if (File.Exists(iconInFolder)) File.SetAttributes(iconInFolder, FileAttributes.Normal);
                    File.Copy(srcIcon, iconInFolder, true);
                    File.SetAttributes(iconInFolder, FileAttributes.Hidden | FileAttributes.System);
                }

                string desktopIni = Path.Combine(workspacePath, "desktop.ini");
                string content =
                    "[.ShellClassInfo]\r\n" +
                    "IconResource=.RacksIcon.ico,0\r\n" +
                    "IconFile=.RacksIcon.ico\r\n" +
                    "IconIndex=0\r\n" +
                    "InfoTip=Racks workspace - files parked in your racks\r\n";
                File.WriteAllText(desktopIni, content);
                File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);

                // The folder must be ReadOnly (or System) for the shell to honor desktop.ini.
                var di = new DirectoryInfo(workspacePath);
                di.Attributes |= FileAttributes.ReadOnly;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set workspace icon: {ex.Message}");
            }
        }

        private static void CreateDesktopLibrary(string workspacePath, string librariesPath, string desktopPath)
        {
            try
            {
                if (!Directory.Exists(librariesPath)) return;

                string libraryFile = Path.Combine(librariesPath, "DesktopWorkspace.library-ms");

                string xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<libraryDescription xmlns=""http://schemas.microsoft.com/windows/2009/library"">
  <name>Desktop (Workspace)</name>
  <version>6</version>
  <isLibraryPinned>true</isLibraryPinned>
  <iconReference>imageres.dll,-183</iconReference>
  <templateInfo>
    <folderType>{{5c4f28b5-f869-4e84-8e60-f11db97c5cc7}}</folderType>
  </templateInfo>
  <searchConnectorDescriptionList>
    <searchConnectorDescription>
      <isDefaultSaveLocation>true</isDefaultSaveLocation>
      <isSupported>false</isSupported>
      <simpleLocation>
        <url>{System.Security.SecurityElement.Escape(desktopPath)}</url>
      </simpleLocation>
    </searchConnectorDescription>
    <searchConnectorDescription>
      <isDefaultNonOwnerSaveLocation>false</isDefaultNonOwnerSaveLocation>
      <isSupported>false</isSupported>
      <simpleLocation>
        <url>{System.Security.SecurityElement.Escape(workspacePath)}</url>
      </simpleLocation>
    </searchConnectorDescription>
  </searchConnectorDescriptionList>
</libraryDescription>";

                File.WriteAllText(libraryFile, xml);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create library: {ex.Message}");
            }
        }
    }
}
