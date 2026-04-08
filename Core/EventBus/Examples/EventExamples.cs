#nullable enable

using Godot;

namespace ReForgeFramework.EventBus.Examples;

public sealed class ExampleEventArgs : IEventArg
{
	public ExampleEventArgs(string message)
	{
		Message = message;
	}

	public string Message { get; }
}

public static class EventExamples
{
	private const string DemoEventId = "reforge.eventbus.demo";
	private const string ManualBusId = "reforge.eventbus.manual.demo";
	private static bool _configured;

	public static void Configure(bool enableDemo = true)
	{
		if (!enableDemo || _configured)
		{
			return;
		}

		_configured = true;

		ReForge.EventBus.RegisterListener<ExampleEventArgs>(DemoEventId, ManualBusId, OnManualEvent);
		ReForge.EventBus.Publish(DemoEventId, new ExampleEventArgs("EventBus ready (manual + attribute)."));

		ReForge.EventBus.UnregisterListener(ManualBusId);
		ReForge.EventBus.Publish(DemoEventId, new ExampleEventArgs("EventBus ready (attribute only)."));
	}

	[ReForge.EventBus.Listener(BusId = "reforge.eventbus.attribute.demo", Id = DemoEventId)]
	public static void OnAttributeEvent(ExampleEventArgs args)
	{
		GD.Print($"[ReForge.EventBus.Example] attribute listener -> {args.Message}");
	}

	private static void OnManualEvent(ExampleEventArgs args)
	{
		GD.Print($"[ReForge.EventBus.Example] manual listener -> {args.Message}");
	}
}
