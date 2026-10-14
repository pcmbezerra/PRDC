# Third-party notices

PRDC has no external NuGet package dependencies.

## Microsoft .NET and Windows Forms

PRDC targets .NET 10 and Windows Forms. The .NET runtime is distributed separately under Microsoft's applicable licenses. See <https://dotnet.microsoft.com/>.

## Windows Remote Desktop ActiveX control

RDP sessions use the `MsRdpClient10NotSafeForScripting` COM class provided by the operating system in `mstscax.dll`. This binary is not included in the PRDC repository or release source.

## REA

[REA](https://github.com/morluto/rea) was used during development to inspect publicly observable application structure and guide an independent implementation. REA is not copied into, linked by, or required at runtime by PRDC.
