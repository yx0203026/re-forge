#nullable enable

using System;
using System.Collections.Generic;
using Godot;

namespace ReForgeFramework.Networking;

internal sealed class ReForgeModelCatalogHandshakeRuntime
{
	private const int HelloBroadcastIntervalMs = 10000;

	private readonly IReForgeNetService _service;
	private readonly Func<long> _utcNowMs;
	private readonly Dictionary<ulong, string> _mismatchReasons = new();
	private long _nextHelloAtMs;

	public ReForgeModelCatalogHandshakeRuntime(IReForgeNetService service, Func<long> utcNowMs)
	{
		_service = service;
		_utcNowMs = utcNowMs;
		LocalSnapshot = ReForgeModelCatalogHasher.Compute();
	}

	public ReForgeModelCatalogHashSnapshot LocalSnapshot { get; }

	public bool HasMismatch => _mismatchReasons.Count > 0;

	public string? LastMismatchReason
	{
		get
		{
			foreach (KeyValuePair<ulong, string> pair in _mismatchReasons)
			{
				return pair.Value;
			}

			return null;
		}
	}

	public void Initialize()
	{
		_service.RegisterMessageHandler<ReForgeModelCatalogHelloMessage>(OnCatalogHello);
		_service.RegisterMessageHandler<ReForgeModelCatalogResultMessage>(OnCatalogResult);

		_nextHelloAtMs = _utcNowMs();
		BroadcastHello();
	}

	public void Update()
	{
		if (!_service.IsConnected)
		{
			return;
		}

		long now = _utcNowMs();
		if (now >= _nextHelloAtMs)
		{
			BroadcastHello();
			_nextHelloAtMs = now + HelloBroadcastIntervalMs;
		}
	}

	public void BroadcastHello()
	{
		_service.Send(new ReForgeModelCatalogHelloMessage
		{
			Hash = LocalSnapshot.Hash,
			CategoryCount = LocalSnapshot.CategoryCount,
			EntryCount = LocalSnapshot.EntryCount,
			SentUtcMs = _utcNowMs(),
		});
	}

	private void OnCatalogHello(ReForgeModelCatalogHelloMessage message, ulong senderId)
	{
		bool accepted = message.Hash == LocalSnapshot.Hash
			&& message.CategoryCount == LocalSnapshot.CategoryCount
			&& message.EntryCount == LocalSnapshot.EntryCount;

		string reason = accepted
			? string.Empty
			: $"Model catalog mismatch. local(hash={LocalSnapshot.Hash},cat={LocalSnapshot.CategoryCount},entry={LocalSnapshot.EntryCount}) remote(hash={message.Hash},cat={message.CategoryCount},entry={message.EntryCount})";

		if (!accepted)
		{
			_mismatchReasons[senderId] = reason;
			GD.PrintErr($"[ReForge.Network] {reason}");
		}
		else
		{
			_mismatchReasons.Remove(senderId);
		}

		_service.SendTo(senderId, new ReForgeModelCatalogResultMessage
		{
			Accepted = accepted,
			LocalHash = LocalSnapshot.Hash,
			RemoteHash = message.Hash,
			LocalCategoryCount = LocalSnapshot.CategoryCount,
			LocalEntryCount = LocalSnapshot.EntryCount,
			Reason = reason,
		});
	}

	private void OnCatalogResult(ReForgeModelCatalogResultMessage message, ulong senderId)
	{
		if (!message.Accepted)
		{
			string reason = string.IsNullOrWhiteSpace(message.Reason)
				? $"Remote rejected local model catalog hash. local={message.RemoteHash}, remote={message.LocalHash}"
				: message.Reason;

			_mismatchReasons[senderId] = reason;
			GD.PrintErr($"[ReForge.Network] {reason}");
		}
		else
		{
			_mismatchReasons.Remove(senderId);
		}
	}
}
