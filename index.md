---
_layout: landing
---

# MeshCS

MeshCS is a powerful C# library for communicating with MeshCore companion radios via serial port.

<div class="row">
<div class="col-md-6">

### Features
- **Object-oriented** - Clean, intuitive class-based API
- **Event-driven** - Subscribe to radio events with familiar C# patterns
- **Async-ready** - Full async/await support for non-blocking operations
- **100% MeshCore coverage** - All companion radio commands supported

</div>
<div class="col-md-6">

### Quick Links
- [Getting Started](docs/Getting-Started.md)
- [API Reference](xref:MeshCS)
- [Examples](docs/Examples.md)
- [GitHub](https://github.com/Litruv/MeshCS)
- [NuGet Package](https://www.nuget.org/packages/MeshCS)

</div>
</div>

## Installation

# [.NET CLI](#tab/dotnet-cli)

```bash
dotnet add package MeshCS
```

# [Package Manager](#tab/package-manager)

```powershell
Install-Package MeshCS
```

# [PackageReference](#tab/package-reference)

```xml
<PackageReference Include="MeshCS" Version="1.0.0" />
```

---

## Quick Example

```csharp
using MeshCS;

// Connect to radio
var radio = new MeshRadio("COM3");
await radio.OpenAsync();

// Subscribe to events
radio.MessageReceived += (s, msg) => Console.WriteLine($"Message: {msg.Text}");
radio.ContactUpdated += (s, contact) => Console.WriteLine($"Contact: {contact.PublicName}");

// Send a message
await radio.SendTextMessageAsync(recipientId, "Hello from MeshCS!");

// Get contacts
var contacts = await radio.GetContactsAsync();
foreach (var contact in contacts)
{
    Console.WriteLine($"{contact.PublicName} - {contact.LastSeen}");
}
```

## Architecture

MeshCS provides three levels of abstraction:

| Layer | Class | Description |
|-------|-------|-------------|
| High-level | `MeshRadio` | Full async API with events and state management |
| Mid-level | `PacketBuilder` / `PacketParser` | Build and parse binary packets |
| Low-level | `CommandCodes` | Raw protocol constants |

## License

MeshCS is released under the [MIT License](https://github.com/Litruv/MeshCS/blob/main/LICENSE).