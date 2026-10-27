using Microsoft.Win32;

public class RegistryHelper
{
    public string regKeyName;
    public RegistryHelper(string regkeyname)
    {
        this.regKeyName = regkeyname;
    }
    // The per-rack writers (WriteToRegistry, WriteMultiLine*, WriteIntArrayToRegistry, KeyExists) were replaced by
    // RackSettingsRepository (hardening P6). What is left reads and writes loose values under the app root; the
    // app-wide switches use AppSettings and MigrationMarkers, and the tests compare those against this old helper.
    public void WriteToRegistryRoot(string keyName, object value)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey($"SOFTWARE\\{regKeyName}"))
            {
                key.SetValue(keyName, value);
            }
        }
        catch { }
    }

}
