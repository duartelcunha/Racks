# Third-party notices

Racks is released under the MIT License (see [`LICENSE.txt`](LICENSE.txt)). It
incorporates and links against the following third-party components. Each
retains its own license, reproduced or referenced below as required.

## Bundled libraries

Direct dependencies (the `PackageReference` items of `Racks/Racks.csproj`):

| Library (NuGet package, version) | License | Source |
| ------- | ------- | ------ |
| WPF UI (`WPF-UI.Tray` 4.3.0, with `WPF-UI` and `WPF-UI.Abstractions`) | MIT License | https://github.com/lepoco/wpfui |
| Windows Community Toolkit (`Microsoft.Toolkit.Uwp.Notifications` 7.1.3) | MIT License | https://github.com/CommunityToolkit/WindowsCommunityToolkit |
| SVG.NET (`Svg` 3.4.8) | Microsoft Public License (Ms-PL) | https://github.com/svg-net/SVG |
| VirtualDesktop (`Slions.VirtualDesktop` 6.9.2) | MIT License | https://github.com/Slion/VirtualDesktop |
| H.Hooks (`H.Hooks` 1.7.0) | MIT License | https://github.com/HavenDV/H.Hooks |
| Microsoft.ML (`Microsoft.ML` 5.0.1, with `Microsoft.ML.CpuMath` and `Microsoft.ML.DataView`; used by Magic Organizer's on-device clustering) | MIT License | https://github.com/dotnet/machinelearning |
| Windows API Code Pack (`Microsoft-WindowsAPICodePack-Shell` 1.1.5, with `-Core`) | Microsoft Software License | https://github.com/contre/Windows-API-Code-Pack-1.1 |

Packages that come in with those and ship in the build (checked against `Racks.deps.json` of the Release build):

| Library (NuGet package, version) | Brought in by | License | Source |
| ------- | ------- | ------- | ------ |
| Newtonsoft.Json 13.0.3 | Microsoft.ML | MIT License | https://github.com/JamesNK/Newtonsoft.Json |
| ExCSS 4.2.3 | Svg | MIT License | https://github.com/TylerBrinks/ExCSS |
| Microsoft.CodeAnalysis.Common and Microsoft.CodeAnalysis.CSharp 4.0.1 | Slions.VirtualDesktop | MIT License | https://github.com/dotnet/roslyn |
| Microsoft.Bcl.AsyncInterfaces 9.0.4 | Microsoft.CodeAnalysis | MIT License | https://github.com/dotnet/runtime |
| System.Numerics.Tensors 9.0.4 | Microsoft.ML | MIT License | https://github.com/dotnet/runtime |

The installer also carries the .NET runtime and the Windows Presentation Foundation libraries (self-contained build), which are
MIT licensed by Microsoft (https://github.com/dotnet/runtime, https://github.com/dotnet/wpf).

Full license texts are available at each project's source link above.
