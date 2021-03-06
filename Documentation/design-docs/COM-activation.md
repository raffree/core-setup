# COM Activation for .NET Core on Windows

## Purpose

In order to more fully support the vast number of existing .NET Framework users in their transition to .NET Core, support of the COM activation scenario in .NET Core is required. Without this support it is not possible for many .NET Framework consumers to even consider transitioning to .NET Core. The intent of this document is to describe aspects of COM activation for a .NET class written for .NET Core. This support includes but is not limited to activation scenarios such as the [`CoCreateInstance()`](https://docs.microsoft.com/en-us/windows/desktop/api/combaseapi/nf-combaseapi-cocreateinstance)API in C/C++ or from within a [Windows Script Host](https://docs.microsoft.com/en-us/windows/desktop/com/using-com-objects-in-windows-script-host) instance.

COM activation in this document is currently limited to in-proc scenarios. Scenarios involving out-of-proc COM activation are deferred.

### Requirements

* Discover all installed versions of .NET Core.
* Load the appropriate version of .NET Core for the class if a .NET Core instance is not running, or validate the currently existing .NET Core instance can satisfy the class requirement.
* Return an [`IClassFactory`](https://docs.microsoft.com/en-us/windows/desktop/api/unknwnbase/nn-unknwnbase-iclassfactory) implementation that will construct an instance of the .NET class.
* Support the discrimination of concurrently loaded CLR versions.

### Environment Matrix

The following list represents an exhaustive activation matrix.

| Server | Client | Current Support |
| --- | --- | :---: |
| COM* | .NET Core | Yes |
| .NET Core | COM* | No |
| .NET Core | .NET Core | No |
| .NET Framework | .NET Core | No |
| .NET Core | .NET Framework | No |

\* 'COM' is used to indicate any COM environment (e.g. C/C++) other than .NET.

## Design

One of the basic issues with the activation of a .NET class within a COM environment is the loading or discovery of an appropriate CLR instance. The .NET Framework addressed this issue through a system wide shim library (described below). The .NET Core scenario has different requirements and limitations on system impact and as such an identical solution may not be optimal or tenable.

### .NET Framework Class COM Activation

The .NET Framework uses a shim library (`mscoree.dll`) to facilitate the loading of the CLR into a process performing activation - one of the many uses of `mscoree.dll`. When .NET Framework 4.0 was released, `mscoreei.dll` was introduced to provide a level of indirection between the system installed shim (`mscoree.dll`) and a specific framework shim as well as to enable side-by-side CLR scenarios. An important consideration of the system wide shim is that of servicing. Servicing `mscoree.dll` is difficult since any process with a loaded .NET Framework instance will have the shim loaded, thus requiring a system reboot in order to service the shim.

During .NET class registration, the shim is identified as the in-proc server for the class. Additional metadata is inserted into the registry to indicate what .NET assembly to load and what type to activate. For example, in addition to the typical [in-proc server](https://docs.microsoft.com/en-us/windows/desktop/com/inprocserver32) registry values the following values are added to the registry for the `TypeLoadException` class.

```
"Assembly"="mscorlib, Version=1.0.5000.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
"Class"="System.TypeLoadException"
"RuntimeVersion"="v1.1.4322"
```

The above registration is typically done with the [`RegAsm.exe`](https://docs.microsoft.com/en-us/dotnet/framework/tools/regasm-exe-assembly-registration-tool) tool. Alternatively, registry scripts can be generated by `RegAsm.exe`.

### .NET Core Class COM Activation

In .NET Core, our intent will be to avoid a system wide shim library. This decision may add additional cost for deployment scenarios, but will reduce servicing and engineering costs by making deployment more explicit and less magic.

The current .NET Core hosting solutions are described in detail at [Documentation/design-docs/host-components.md](https://github.com/dotnet/core-setup/blob/master/Documentation/design-docs/host-components.md). Along with the existing hosts an additional activation host library will be added. This library (henceforth identified as 'shim') will export the required functions for COM class activation and registration and act in a way similar to `mscoree.dll`.

>[`HRESULT DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID *ppv);`](https://docs.microsoft.com/en-us/windows/desktop/api/combaseapi/nf-combaseapi-dllgetclassobject)

>[`HRESULT DllCanUnloadNow();`](https://docs.microsoft.com/en-us/windows/desktop/api/combaseapi/nf-combaseapi-dllcanunloadnow)

>[`HRESULT DllRegisterServer();`](https://msdn.microsoft.com/en-us/library/windows/desktop/ms682162(v=vs.85).aspx)

>[`HRESULT DllUnregisterServer();`](https://msdn.microsoft.com/en-us/library/windows/desktop/ms691457(v=vs.85).aspx)

When `DllGetClassObject()` is called in a COM activation scenario, the following will occur:

1) Determine additional registration information needed for activation.
    * The shim will check for an embedded manifest. If the shim does not contain an embedded manifest, the shim will check if a file with the `<shim_name>.clsidmap` naming format exists adjacent to it. Build tooling will be expected to handle shim customization, including renaming the shim to be based on the managed assembly's name (e.g. `NetComServer.dll` could have a custom shim called `NetComServer.shim.dll`).
    * The manifest will contain a mapping from [`CLSID`](https://docs.microsoft.com/en-us/windows/desktop/com/com-class-objects-and-clsids) to managed assembly name and the [Fully-Qualified Name](https://docs.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/specifying-fully-qualified-type-names) for the type. The exact format of this manifest is an implementation detail, but will be identical whether it is embedded or a loose file.
    * The manifest will define an exhaustive list of .NET classes it is permitted to provide.
    * If a [`.runtimeconfig.json`](https://github.com/dotnet/cli/blob/master/Documentation/specs/runtime-configuration-file.md) file exists adjacent to the shim assembly (`<shim_name>.runtimeconfig.json`), that file will be used to describe CLR configuration details. The documentation for the `.runtimeconfig.json` format defines under what circumstances this file may be optional.
1) Using the existing `hostfxr` library, attempt to discover the desired CLR and target [framework](https://docs.microsoft.com/en-us/dotnet/core/packages#frameworks).
    * If a CLR is active with the process, the requested CLR version will be validated against that CLR. If version satisfiability fails, activation will fail.
    * If a CLR is **not** active with the process, an attempt will be made to create a satisfying CLR instance. Failure to create an instance will result in activation failure.
1) A request to the CLR will be made via a new method for class activation within a COM environment.
    * The ability to load the assembly and create an `IClassFactory` instance will require exposing a new function that can be called from `hostfxr`.
    * Example of a possible API in `System.Private.CoreLib` on a new `ComActivator` class in the `System.Runtime.InteropServices` namespace:
        ``` csharp
        [StructLayout(LayoutKind.Sequential)]
        public struct ComActivationContextInternal
        {
            public Guid ClassId;
            public Guid InterfaceId;
            public IntPtr AssemblyNameBuffer;
            public IntPtr TypeNameBuffer;
            public IntPtr ClassFactoryDest;
        }

        public static class ComActivator
        {
            ...
            public static int GetClassFactoryForTypeInternal(ref ComActivationContextInternal context);
            ...
        }
        ```
        Note this API would not be exposed outside of `System.Private.CoreLib`.
    * The loading of the assembly will take place in a new [`AssemblyLoadContext`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext?view=netcore-2.1) for dependency isolation.
    * **Complete details on ALC semantics as they relate to class activation and lifetime are TBD**
1) The `IClassFactory` instance will be returned to the caller of `DllGetClassObject()`.

The `DllCanUnloadNow()` function will always return `S_FALSE` indicating the shim is never able to be unloaded. This matches .NET Framework semantics and can be adjusted in the future if needed.

The `DllRegisterServer()` and `DllUnregisterServer()` functions will adhere to the [COM registration contract](https://docs.microsoft.com/en-us/windows/desktop/com/classes-and-servers) and enable registration and unregistration of the classes defined in the `.clsidmap` manifest.

#### Class Registration

Two options exist for registration and are a function of the intent of the class's author. The .NET Core platform will impose the deployment of a shim instance with a `.clsidmap` manifest. In order to address potential security concerns, the .NET Core tool chain will also permit the creation of a customized shim instance with an embedded `.clsidmap`. This customized shim will allow for the implicit signing of the `.clsidmap` manifest.

##### Registry

Class registration in the registry for .NET Core classes is greatly simplified and is now identical to that of a non-managed COM class. This is possible due to the pressence of the aforementioned `.clsidmap` manifest. The application developer will be able to use the traditional [`regsvr32.exe`](https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/regsvr32) tool for class registration.

##### Registration-Free

[RegFree COM for .NET](https://docs.microsoft.com/en-us/dotnet/framework/interop/configure-net-framework-based-com-components-for-reg) is another style of registration, but does not require registry access. This approach is complicated by the use of [application manifests](https://docs.microsoft.com/en-us/windows/desktop/SbsCs/application-manifests), but does have benefits for limiting environment impact and simplifying deployment. A severe limitation of this approach is that in order to use RegFree COM with a .NET class, the Window OS assumes the use of `mscoree.dll` for the in-proc server. Without a change in the Windows OS, this assumption in the RegFree .NET scenario makes the existing manifest approach a broken scenario for .NET Core.

An example of a RegFree manifest for a .NET Framework class is below - note the absence of specifying a hosting server library (i.e. `mscoree.dll` is implied for the `clrClass` element).

``` xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
    <assemblyIdentity
        type="win32"
        name="NetComServer"
        version="1.0.0.0" />

    <clrClass
        clsid="{3C58BBC9-3966-4B58-8EE2-398CBBC9FDC4}"
        name="NetComServer.Server"
        threadingModel="Both"
        runtimeVersion="v4.0.30319">
    </clrClass>
</assembly>
```

Due to the above issues with traditional RegFree manifests and .NET classes, an alternative system must be employed to enable a low-impact style of class registration for .NET Core.

The proposed alternative for RegFree is as follows:

1) The native application will still define an application manifest, but instead of specifying the managed assembly as a dependency the application will define the shim as a dependent assembly.
    ``` xml
    <?xml version="1.0" encoding="utf-8" standalone="yes" ?>
    <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
        <assemblyIdentity
            type="win32"
            name="COMClientPrimitives"
            version="1.0.0.0" />

        <dependency>
            <dependentAssembly>
                <!-- RegFree COM - CoreCLR Shim -->
                <assemblyIdentity
                    type="win32"
                    name="CoreShim.X"
                    version="1.0.0.0" />
            </dependentAssembly>
        </dependency>
    </assembly>
    ```
1) The user would then also define a [SxS](https://docs.microsoft.com/en-us/windows/desktop/sbscs/about-side-by-side-assemblies-) manifest for the shim. Both the SxS manifest _and_ the shim library will need to be app-local for the scenario to work. Note that the application developer is responsible for defining the shim's manifest. An example shim manifest is defined below and with it the SxS logic would naturally know to query the shim for the desired class. Note that multiple `comClass` tags can be added.
    ``` xml
    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
        <assemblyIdentity
            type="win32"
            name="CoreShim.X"
            version="1.0.0.0" />

        <file name="CoreShim.dll">
            <!-- NetComServer.Server -->
            <comClass
                clsid="{3C58BBC9-3966-4B58-8EE2-398CBBC9FDC4}"
                threadingModel="Both" />
        </file>
    </assembly>
    ```
1) When the native application starts up, its SxS manifest will be read and dependency assemblies discovered. Exported COM classes will also be registered in the process.
1) At runtime, during a class activation call, COM will consult the SxS registration and discover the shim library should be used to load the class. The shim will then consult the `.clsidmap` manifest - first checking if the manifest is embedded - and attempt to map the `CLSID` to a managed assembly type tuple.

The [`dotnet.exe`][dotnet_link] tool could be made to generate the SxS `.manifest` files.

## Compatibility Concerns

* Side-by-side concerns with the registration of classes that are defined in both .NET Framework and .NET Core.
    - i.e. Both classes have identical [`Guid`](https://docs.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.guidattribute?view=netcore-2.1) values.
* RegFree COM will not work the same between .NET Framework and .NET Core.
    - See details above.
* Servicing of the .NET Framework shim (`mscoree.dll`) was done at the system level. In the .NET Core scenario the onus is on the application developer to have a servicing process in place for the shim.

## References

[Calling COM Components from .NET Clients](https://msdn.microsoft.com/en-us/library/ms973800.aspx)

[Calling a .NET Component from a COM Component](https://msdn.microsoft.com/en-us/library/ms973802.aspx)

[Using COM Types in Managed Code](https://docs.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/3y76b69k%28v%3dvs.100%29)

[Exposing .NET Framework Components to COM](https://docs.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/zsfww439(v%3dvs.100))

<!-- Common links -->
[dotnet_link]: https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet?tabs=netcore21
[com_activation_context]: https://docs.microsoft.com/en-us/windows/desktop/sbscs/activation-contexts