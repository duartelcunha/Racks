using Microsoft.Win32;
using System.Diagnostics;

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
    public object? ReadKeyValueRoot(string keyName)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{regKeyName}")!)
            {
                if (key != null)
                {
                    object value = key.GetValue(keyName)!;
                    if (value != null)
                    {
                        if (value is bool)
                        {
                            Debug.WriteLine($"returned bool for {keyName}");
                            return (bool)value;
                        }
                        else if (bool.TryParse(value.ToString(), out bool boolValue))
                        {
                            Debug.WriteLine($"returned Parsed bool for {keyName}");
                            return boolValue;
                        }
                        else
                        {
                            Debug.WriteLine($"returned string for {keyName}");
                            return (string)value;
                        }

                    }
                }
            }
            Debug.WriteLine($"couldn't return for {keyName}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error ReadKeyValueRoot: " + ex.Message);
            return null;
        }
    }
    public object? ReadKeyValueRootInt(string keyName)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey($"SOFTWARE\\{regKeyName}")!)
            {
                if (key != null)
                {
                    return Int32.Parse(key.GetValue(keyName)?.ToString() ?? "0");
                }
            }

            Debug.WriteLine($"couldn't return value for {keyName}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Error ReadKeyValueRootInt: " + ex.Message);
            return null;
        }
    }



}

