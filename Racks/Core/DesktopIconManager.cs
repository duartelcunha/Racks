using System;
using System.IO;

namespace Racks.Core
{
    public static class DesktopIconManager
    {
        public static string RacksWorkspacePath { get; private set; } = string.Empty;

        public static void Initialize()
        {
            try
            {
                // Create the workspace folder
                RacksWorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RacksWorkspace");
                if (!Directory.Exists(RacksWorkspacePath))
                {
                    DirectoryInfo di = Directory.CreateDirectory(RacksWorkspacePath);
                    di.Attributes |= FileAttributes.Hidden; // Hide the workspace folder itself from the user's home directory so it's not messy
                }

                ApplyWorkspaceIcon();
                CreateDesktopLibrary();
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
        private static void ApplyWorkspaceIcon()
        {
            try
            {
                string iconInFolder = Path.Combine(RacksWorkspacePath, ".RacksIcon.ico");
                // Write the app's own icon (embedded in the exe) into the folder once. Extract
                // from the running exe so we don't depend on a loose icon file being deployed.
                if (!File.Exists(iconInFolder))
                {
                    string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Racks.exe");
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (ico == null) return;
                    using var fs = new FileStream(iconInFolder, FileMode.Create, FileAccess.Write);
                    ico.Save(fs);
                    File.SetAttributes(iconInFolder, FileAttributes.Hidden | FileAttributes.System);
                }

                string desktopIni = Path.Combine(RacksWorkspacePath, "desktop.ini");
                string content =
                    "[.ShellClassInfo]\r\n" +
                    "IconResource=.RacksIcon.ico,0\r\n" +
                    "IconFile=.RacksIcon.ico\r\n" +
                    "IconIndex=0\r\n" +
                    "InfoTip=Racks workspace - files parked in your racks\r\n";
                File.WriteAllText(desktopIni, content);
                File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System);

                // The folder must be ReadOnly (or System) for the shell to honor desktop.ini.
                var di = new DirectoryInfo(RacksWorkspacePath);
                di.Attributes |= FileAttributes.ReadOnly;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set workspace icon: {ex.Message}");
            }
        }

        private static void CreateDesktopLibrary()
        {
            try
            {
                string librariesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Libraries");
                if (!Directory.Exists(librariesPath)) return;

                string libraryFile = Path.Combine(librariesPath, "DesktopWorkspace.library-ms");
                
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

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
        <url>{desktopPath}</url>
      </simpleLocation>
    </searchConnectorDescription>
    <searchConnectorDescription>
      <isDefaultNonOwnerSaveLocation>false</isDefaultNonOwnerSaveLocation>
      <isSupported>false</isSupported>
      <simpleLocation>
        <url>{RacksWorkspacePath}</url>
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

        public static void StartHook()
        {
            // No-op. Hook is removed.
        }

        public static void StopHook()
        {
            // No-op. Hook is removed.
        }

        public static void SetHiddenFilesForInstance(object instance, System.Collections.Generic.IEnumerable<string> items)
        {
            // Desktop Filter logic has been replaced by physical file moving to RacksWorkspace.
            // This function is kept to avoid compilation errors during transition, but it does nothing.
        }
    }
}
