# EventSource Conversion Skill: Self-Describing to AOT-Compatible Manifest-Based

## Overview

This skill converts self-describing EventSource implementations (using `Write<T>` with `EventSourceOptions` and `[EventData]` structs) to **AOT-compatible** manifest-based EventSource implementations (using `WriteEvent` with `[Event]` attributes).

**AOT Compatibility Requirements:**
- Every `[Event]` method must only use **primitive types** as parameters (see Step 6 for allowed types)
- The EventSource class must suppress trimming/AOT warnings with appropriate attributes

## When to Use

Use this skill when you need to convert EventSource code that:
- Uses `NuGetEventSource.Instance.Write(eventName, eventOptions, data)` pattern
- Uses `[EventData]` record structs for event data
- Uses `EventSourceOptions` to specify Keywords, Opcode, ActivityOptions

## Conversion Process

### Step 1: Identify All Events in the TraceEvents Class

Look for patterns like:
```csharp
private static class TraceEvents
{
    private const string EventNameSomething = "Namespace/Something";
    
    public static void SomethingStart(...) { ... }
    public static void SomethingStop(...) { ... }
    
    [EventData]
    private record struct SomeEventData(...);
}
```

### Step 2: Create or Update the EventSource Class

Create a new EventSource class (or update existing one) that:
1. Inherits from `EventSource`
2. Has `[EventSource(Name = "Microsoft-NuGet")]` attribute (preserve existing name!)
3. Has a singleton `Instance` property
4. Defines `Keywords` and `Tasks` nested classes
5. Has `[Event(...)]` attributed methods with **primitive types only**
6. **Each `[Event]` method** must have AOT/trimming suppression attributes (see Step 5)

### Step 3: Assign Event IDs

Create stable numeric event IDs:
- Group related Start/Stop events
- Start events get odd IDs (1, 3, 5...)
- Stop events get next sequential ID (2, 4, 6...)
- Keep IDs stable across versions

```csharp
private const int SomethingStartEventId = 1;
private const int SomethingStopEventId = 2;
```

### Step 4: Define Tasks for Start/Stop Correlation

Each Start/Stop pair needs a Task:
```csharp
public static class Tasks
{
    public const EventTask Something = (EventTask)1;
    public const EventTask SomethingElse = (EventTask)2;
}
```

### Step 5: Convert Event Methods

**Before (self-describing):**
```csharp
private const string EventNameSomething = "Component/Something";

public static void SomethingStart(string path, string projectFullPath)
{
    var eventOptions = new EventSourceOptions
    {
        ActivityOptions = EventActivityOptions.Detachable,
        Keywords = NuGetEventSource.Keywords.Performance | NuGetEventSource.Keywords.SdkResolver,
        Opcode = EventOpcode.Start
    };

    NuGetEventSource.Instance.Write(EventNameSomething, eventOptions, new SomethingEventData(path, projectFullPath));
}

[EventData]
private record struct SomethingEventData(string Path, string ProjectFullPath);
```

**After (AOT-compatible manifest-based):**
```csharp
private const int SomethingStartEventId = 1;

[Event(SomethingStartEventId, 
       Level = EventLevel.Informational, 
       Keywords = Keywords.SdkResolver | Keywords.Performance, 
       Opcode = EventOpcode.Start, 
       Task = Tasks.Something)]
[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Event parameters are primitive types only")]
[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Event parameters are primitive types only")]
public void SomethingStart(string path, string projectFullPath)
{
    WriteEvent(SomethingStartEventId, path ?? string.Empty, projectFullPath ?? string.Empty);
}
```

### Step 6: Handle Type Conversions (AOT-Compatible Primitives Only)

**CRITICAL FOR AOT COMPATIBILITY:** `[Event]` methods must only use primitive types that `WriteEvent` supports natively. Using non-primitive types will cause AOT/trimming issues.

**Allowed primitive types for `[Event]` method parameters:**
- `int`, `long`, `byte`, `short`
- `float`, `double`
- `string`
- `bool` (but convert to `int` for `WriteEvent` call)
- `Guid` (but convert to `string` for `WriteEvent` call)

**NOT allowed (will break AOT):**
- Custom structs or classes
- Arrays (except `byte[]` in some overloads)
- Enums (convert to `int`)
- Nullable types
- `object`

Convert types as needed:

| Original Type | Parameter Type | WriteEvent Conversion |
|--------------|----------------|----------------------|
| `bool` | `int` | Pass directly (already converted at call site) |
| `Guid` | `string` | Pass directly (already converted at call site) |
| `null` strings | `string` | `value ?? string.Empty` |
| `enum` | `int` | Pass directly (already converted at call site) |
| Complex objects | Flatten to primitives | Extract properties at call site |

### Step 7: Update Call Sites

Replace calls from:
```csharp
TraceEvents.SomethingStart(path, projectFullPath);
```

To:
```csharp
MyEventSource.Instance.SomethingStart(path, projectFullPath);
```

### Step 8: Remove Old TraceEvents Class

After migration, remove:
- The `TraceEvents` static class
- All `[EventData]` record structs
- The `EventSourceOptions` usage

## Complete Example

### Before (Self-Describing)

```csharp
// In NuGetSdkResolver.cs
private static class TraceEvents
{
    private const string EventNameResolve = "SdkResolver/Resolve";

    public static void ResolveStart(SdkReference sdkReference)
    {
        var eventOptions = new EventSourceOptions
        {
            ActivityOptions = EventActivityOptions.Detachable,
            Keywords = NuGetEventSource.Keywords.SdkResolver | NuGetEventSource.Keywords.Performance,
            Opcode = EventOpcode.Start
        };

        NuGetEventSource.Instance.Write(EventNameResolve, eventOptions, new ResolveEventData(sdkReference.Name, sdkReference.Version));
    }

    public static void ResolveStop(SdkReference sdkReference)
    {
        var eventData = new EventSourceOptions
        {
            ActivityOptions = EventActivityOptions.Detachable,
            Keywords = NuGetEventSource.Keywords.SdkResolver | NuGetEventSource.Keywords.Performance,
            Opcode = EventOpcode.Stop
        };

        NuGetEventSource.Instance.Write(EventNameResolve, eventData, new ResolveEventData(sdkReference.Name, sdkReference.Version));
    }

    [EventData]
    private record struct ResolveEventData(string Name, string Version);
}
```

### After (AOT-Compatible Manifest-Based)

```csharp
// In SdkResolverEventSource.cs
using System.Diagnostics.CodeAnalysis;

[EventSource(Name = "Microsoft-NuGet-SdkResolver")]
internal sealed partial class SdkResolverEventSource : EventSource
{
    public static readonly SdkResolverEventSource Instance = new SdkResolverEventSource();

    public static class Keywords
    {
        public const EventKeywords SdkResolver = (EventKeywords)16;
        public const EventKeywords Performance = (EventKeywords)8;
    }

    public static class Tasks
    {
        public const EventTask Resolve = (EventTask)1;
    }

    private const int ResolveStartEventId = 1;
    private const int ResolveStopEventId = 2;

    [Event(ResolveStartEventId, 
           Level = EventLevel.Informational, 
           Keywords = Keywords.SdkResolver | Keywords.Performance, 
           Opcode = EventOpcode.Start, 
           Task = Tasks.Resolve)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Event parameters are primitive types only")]
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Event parameters are primitive types only")]
    public void ResolveStart(string name, string version)
    {
        WriteEvent(ResolveStartEventId, name ?? string.Empty, version ?? string.Empty);
    }

    [Event(ResolveStopEventId, 
           Level = EventLevel.Informational, 
           Keywords = Keywords.SdkResolver | Keywords.Performance, 
           Opcode = EventOpcode.Stop, 
           Task = Tasks.Resolve)]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Event parameters are primitive types only")]
    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Event parameters are primitive types only")]
    public void ResolveStop(string name, string version)
    {
        WriteEvent(ResolveStopEventId, name ?? string.Empty, version ?? string.Empty);
    }
}

// In NuGetSdkResolver.cs - Update call sites:
// Before: TraceEvents.ResolveStart(sdkReference);
// After:  SdkResolverEventSource.Instance.ResolveStart(sdkReference.Name, sdkReference.Version);
```

## Migration Checklist

For each file with self-describing EventSource usage:

- [ ] Identify all event names (const strings like `EventNameXxx`)
- [ ] Identify all `[EventData]` record structs
- [ ] Create/update EventSource class with `[EventSource(Name = "...")]`
- [ ] **Add AOT suppression attributes** (`UnconditionalSuppressMessage` for IL2026 and IL3050)
- [ ] Define numeric event ID constants (stable, sequential)
- [ ] Define `Tasks` for Start/Stop event pairs
- [ ] Create `[Event(...)]` attributed methods with:
  - Event ID
  - `Level = EventLevel.Informational` (default)
  - `Keywords` (same as original)
  - `Opcode` (Start/Stop)
  - `Task` (for Start/Stop correlation)
  - **Primitive types only** for parameters
- [ ] Flatten `[EventData]` struct fields to method parameters
- [ ] **Verify all parameters are AOT-compatible primitives** (int, long, string, etc.)
- [ ] Handle null strings with `?? string.Empty`
- [ ] Convert `bool` to `int` for WriteEvent compatibility
- [ ] Convert `enum` to `int` for AOT compatibility
- [ ] Update all call sites to use new EventSource instance
- [ ] Remove old `TraceEvents` class and `[EventData]` structs
- [ ] Verify ETW events with PerfView or logman

## Breaking Change Considerations

When converting, be aware of consumer impact:

1. **Preserve EventSource Name** - This preserves the Provider GUID
2. **Document Event ID Changes** - Consumers filtering by ID need to update
3. **Keywords May Change** - Extra session bits may be added
4. **Recommend Task+Opcode Filtering** - More stable than EventID filtering

## NuGet-Specific Keywords Reference

```csharp
public static class Keywords
{
    public const EventKeywords Common = (EventKeywords)1;
    public const EventKeywords Configuration = (EventKeywords)2;
    public const EventKeywords Logging = (EventKeywords)4;
    public const EventKeywords Performance = (EventKeywords)8;
    public const EventKeywords SdkResolver = (EventKeywords)16;
    public const EventKeywords Restore = (EventKeywords)32;
}
```

## Files Requiring Conversion in NuGet.Client

The following files contain self-describing EventSource patterns that need conversion:

1. `src/NuGet.Core/Microsoft.Build.NuGetSdkResolver/NuGetSdkResolver.cs` - TraceEvents class
2. `src/NuGet.Core/Microsoft.Build.NuGetSdkResolver/NuGetSdkLogger.cs` - TraceEvents class
3. `src/NuGet.Core/NuGet.Common/Migrations/MigrationRunner.cs` - TraceEvents class
4. `src/NuGet.Core/NuGet.Configuration/Settings/SettingsFile.cs` - TraceEvents class
5. `src/NuGet.Core/NuGet.Configuration/Settings/SettingsLoadingContext.cs` - TraceEvents class
6. `src/NuGet.Core/NuGet.Commands/RestoreCommand/RestoreCommand.cs` - TraceEvents class
7. `src/NuGet.Core/NuGet.Commands/RestoreCommand/RestoreResult.cs` - TraceEvents class
8. `src/NuGet.Core/NuGet.Commands/RestoreCommand/RestoreRunner.cs` - TraceEvents class
9. `src/NuGet.Core/NuGet.Commands/RestoreCommand/DependencyGraphResolver.cs` - TraceEvents class

## Reference Implementation

See `src/NuGet.Core/Microsoft.Build.NuGetSdkResolver/SdkResolverEventSource.cs` for a working example of manifest-based EventSource in this repository.
