﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hermes;
using Hermes.Flows;
using Hermes.Packets;
using Hermes.Storage;
using Moq;
using Xunit;

namespace Tests.Flows
{
	public class PublishSenderFlowSpec
	{
		[Fact]
		public async Task when_sending_publish_with_qos1_and_publish_ack_is_not_received_then_publish_is_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> (c => c.WaitingTimeoutSecs == 1);
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new PublishSenderFlow (connectionProvider.Object, 
				sessionRepository.Object, packetIdentifierRepository, configuration);

			var topic = "foo/bar";
			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.AtLeastOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);
			connectionProvider.Setup (m => m.IsConnected (It.IsAny<string> ())).Returns (true);
			connectionProvider.Setup (m => m.GetConnection (It.IsAny<string> ())).Returns (channel.Object);

			await flow.SendPublishAsync (clientId, publish);

			Thread.Sleep (2000);

			connectionProvider.Verify (m => m.IsConnected (It.Is<string> (s => s == clientId)));
			connectionProvider.Verify (m => m.GetConnection (It.IsAny<string> ()));
			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is Publish  && 
				((Publish)p).Topic == topic && 
				((Publish)p).QualityOfService == QualityOfService.AtLeastOnce &&
				((Publish)p).PacketId == packetId)), Times.AtLeast(2));
		}

		[Fact]
		public async Task when_sending_publish_with_qos2_and_publish_received_is_not_received_then_publish_is_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> (c => c.WaitingTimeoutSecs == 1);
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new PublishSenderFlow (connectionProvider.Object, 
				sessionRepository.Object, packetIdentifierRepository, configuration);

			var topic = "foo/bar";
			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);
			connectionProvider.Setup (m => m.IsConnected (It.IsAny<string> ())).Returns (true);
			connectionProvider.Setup (m => m.GetConnection (It.IsAny<string> ())).Returns (channel.Object);

			await flow.SendPublishAsync (clientId, publish);

			Thread.Sleep (2000);

			connectionProvider.Verify (m => m.IsConnected (It.Is<string> (s => s == clientId)));
			connectionProvider.Verify (m => m.GetConnection (It.IsAny<string> ()));
			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is Publish  && 
				((Publish)p).Topic == topic && 
				((Publish)p).QualityOfService == QualityOfService.ExactlyOnce &&
				((Publish)p).PacketId == packetId)), Times.AtLeast(2));
		}

		[Fact]
		public async Task when_sending_publish_received_then_publish_release_is_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> (c => c.WaitingTimeoutSecs == 10);
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			connectionProvider.Setup (m => m.IsConnected (It.IsAny<string> ())).Returns (true);

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new PublishSenderFlow (connectionProvider.Object, 
				sessionRepository.Object, packetIdentifierRepository, configuration);

			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishReceived = new PublishReceived (packetId);
			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			connectionProvider.Setup (m => m.GetConnection (It.Is<string> (s => s == clientId))).Returns (channel.Object);

			await flow.ExecuteAsync (clientId, publishReceived, channel.Object);

			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishRelease 
				&& (p as PublishRelease).PacketId == packetId)), Times.Once);
		}

		[Fact]
		public async Task when_sending_publish_received_and_no_complete_is_sent_after_receiving_publish_release_then_publish_release_is_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> (c => c.WaitingTimeoutSecs == 1);
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			connectionProvider.Setup (m => m.IsConnected (It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new PublishSenderFlow (connectionProvider.Object, 
				sessionRepository.Object, packetIdentifierRepository, configuration);

			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishReceived = new PublishReceived (packetId);
			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			connectionProvider.Setup (m => m.GetConnection (It.Is<string> (s => s == clientId))).Returns (channel.Object);

			await flow.ExecuteAsync (clientId, publishReceived, channel.Object);

			Thread.Sleep (2000);

			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishRelease 
				&& (p as PublishRelease).PacketId == packetId)), Times.AtLeast(2));
		}

		[Fact]
		public async Task when_sending_publish_received_and_complete_is_sent_after_receiving_publish_release_then_publish_release_is_not_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> (c => c.WaitingTimeoutSecs == 2);
			var connectionProvider = new Mock<IConnectionProvider> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			connectionProvider.Setup (m => m.IsConnected (It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

			var flow = new PublishSenderFlow (connectionProvider.Object, 
				sessionRepository.Object, packetIdentifierRepository, configuration);

			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishReceived = new PublishReceived (packetId);

			var receiver = new Subject<IPacket> ();
			var sender = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			sender.OfType<PublishRelease>().Subscribe (release => {
				receiver.OnNext (new PublishComplete (release.PacketId));
			});

			channel.Setup (c => c.Receiver).Returns (receiver);
			channel.Setup (c => c.Sender).Returns (sender);
			channel.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => sender.OnNext (packet))
				.Returns(Task.Delay(0));

			connectionProvider.Setup (m => m.GetConnection (It.Is<string> (s => s == clientId))).Returns (channel.Object);

			await flow.ExecuteAsync (clientId, publishReceived, channel.Object);

			Thread.Sleep (2000);

			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishRelease 
				&& (p as PublishRelease).PacketId == packetId)), Times.Once);
		}

		[Fact]
		public async Task when_sending_publish_ack_then_packet_identifier_is_deleted()
		{
			var clientId = Guid.NewGuid().ToString();

			var configuration = Mock.Of<ProtocolConfiguration> ();
			var connectionProvider = Mock.Of<IConnectionProvider> ();
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var retainedRepository = Mock.Of<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = new Mock<IRepository<PacketIdentifier>> ();
			
			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishAck = new PublishAck (packetId);
			var flow = new PublishSenderFlow (connectionProvider, 
				sessionRepository.Object, packetIdentifierRepository.Object, configuration);
			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publishAck, channel.Object);

			packetIdentifierRepository.Verify (r => r.Delete (It.IsAny<Expression<Func<PacketIdentifier, bool>>> ()));
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket>()), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_complete_then_packet_identifier_is_deleted()
		{
			var clientId = Guid.NewGuid().ToString();

			var configuration = Mock.Of<ProtocolConfiguration> ();
			var connectionProvider = Mock.Of<IConnectionProvider> ();
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var retainedRepository = Mock.Of<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdentifierRepository = new Mock<IRepository<PacketIdentifier>> ();

			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishComplete = new PublishComplete (packetId);
			var flow = new PublishSenderFlow (connectionProvider, 
				sessionRepository.Object, packetIdentifierRepository.Object, configuration);
			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publishComplete, channel.Object);

			packetIdentifierRepository.Verify (r => r.Delete (It.IsAny<Expression<Func<PacketIdentifier, bool>>> ()));
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket>()), Times.Never);
		}
	}
}