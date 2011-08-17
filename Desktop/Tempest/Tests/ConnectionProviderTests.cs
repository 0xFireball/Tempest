﻿//
// ConnectionProviderTests.cs
//
// Author:
//   Eric Maupin <me@ermau.com>
//
// Copyright (c) 2011 Eric Maupin
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using NUnit.Framework;
using Tempest.InternalProtocol;

namespace Tempest.Tests
{
	public abstract class ConnectionProviderTests
	{
		protected IConnectionProvider provider;
		protected readonly List<IClientConnection> connections = new List<IClientConnection>();

		[SetUp]
		protected void Setup()
		{
			Trace.WriteLine ("Entering", "Setup");
			this.provider = SetUp();
			Trace.WriteLine ("Exiting", "Setup");
		}

		[TearDown]
		protected void TearDown()
		{
			Trace.WriteLine ("Entering", "TearDown");

			if (this.provider != null)
				this.provider.Dispose();

			lock (this.connections)
			{
			    foreach (var c in this.connections)
			        c.Dispose();

			    this.connections.Clear();
			}

			Trace.WriteLine ("Exiting", "TearDown");
		}

		protected abstract EndPoint EndPoint { get; }
		protected abstract MessageTypes MessageTypes { get; }

		protected abstract IConnectionProvider SetUp();
		protected abstract IConnectionProvider SetUp (IEnumerable<Protocol> protocols);

		protected abstract IClientConnection SetupClientConnection();
		protected virtual IClientConnection SetupClientConnection (out IAsymmetricKey key)
		{
			key = null;
			return SetupClientConnection();
		}

		protected abstract IClientConnection SetupClientConnection (IEnumerable<Protocol> protocols);

		protected IClientConnection GetNewClientConnection()
		{
			var c = SetupClientConnection();

			lock (this.connections)
			    this.connections.Add (c);

			return c;
		}

		protected IClientConnection GetNewClientConnection (out IAsymmetricKey key)
		{
			var c = SetupClientConnection (out key);

			lock (this.connections)
				this.connections.Add (c);

			return c;
		}

		[Test]
		public void InvalidProtocolVersion()
		{
			TearDown();

			this.provider = SetUp (new[] { new Protocol (5, 5, 4) });
			this.provider.Start (MessageTypes);

			var client = SetupClientConnection (new[] { new Protocol (5, 3) });

			var test = new AsyncTest (e => Assert.AreEqual (ConnectionResult.IncompatibleVersion, ((DisconnectedEventArgs)e).Result));
			client.Connected += test.FailHandler;
			client.Disconnected += test.PassHandler;

			client.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test]
		public void OlderCompatibleVersion()
		{
			TearDown();

			this.provider = SetUp (new[] { new Protocol (5, 5, 4) });
			this.provider.Start (MessageTypes);

			var client = SetupClientConnection (new[] { new Protocol (5, 4) });

			var test = new AsyncTest();
			client.Connected += test.PassHandler;
			client.Disconnected += test.FailHandler;

			client.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test]
		public void ConnectionlessSupport()
		{
			EventHandler<ConnectionlessMessageEventArgs> cmr = (sender, e) => { };

			if (this.provider.SupportsConnectionless)
			{
				Assert.DoesNotThrow (() => this.provider.ConnectionlessMessageReceived += cmr);
				Assert.DoesNotThrow (() => this.provider.ConnectionlessMessageReceived -= cmr);
				Assert.DoesNotThrow (() => this.provider.SendConnectionlessMessage (new MockMessage (), new IPEndPoint (IPAddress.Loopback, 42)));
			}
			else
			{
				Assert.Throws<NotSupportedException> (() => this.provider.ConnectionlessMessageReceived += cmr);
				Assert.Throws<NotSupportedException> (() => this.provider.SendConnectionlessMessage (new MockMessage (), new IPEndPoint (IPAddress.Loopback, 42)));
				Assert.Throws<NotSupportedException> (() => this.provider.Start (MessageTypes.Unreliable));
			}
		}

		[Test]
		public void SendConnectionlessMessageNull()
		{
			Assert.Throws<ArgumentNullException> (() => this.provider.SendConnectionlessMessage (null, new IPEndPoint (IPAddress.Loopback, 42)));
			Assert.Throws<ArgumentNullException> (() => this.provider.SendConnectionlessMessage (new MockMessage (), null));
		}

		[Test]
		public void StartRepeatedly()
		{
			// *knock knock knock* Penny
			Assert.DoesNotThrow (() => this.provider.Start (MessageTypes));
			// *knock knock knock* Penny
			Assert.DoesNotThrow (() => this.provider.Start (MessageTypes));
			// *knock knock knock* Penny
			Assert.DoesNotThrow (() => this.provider.Start (MessageTypes));
		}

		[Test]
		public void StopRepeatedly()
		{
			// *knock knock knock* Sheldon
			Assert.DoesNotThrow (() => this.provider.Stop());
			// *knock knock knock* Sheldon
			Assert.DoesNotThrow (() => this.provider.Stop());
			// *knock knock knock* Sheldon
			Assert.DoesNotThrow (() => this.provider.Stop());
		}

		[Test, Repeat (3)]
		public void IsRunning()
		{
			Assert.IsFalse (provider.IsRunning);

			provider.Start (MessageTypes);
			Assert.IsTrue (provider.IsRunning);

			provider.Stop();
			Assert.IsFalse (provider.IsRunning);
		}

		[Test, Repeat (3)]
		public void ConnectionMade()
		{
			this.provider.Start (MessageTypes);

			var test = new AsyncTest();
			this.provider.ConnectionMade += test.PassHandler;
			var c = GetNewClientConnection();
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ConnectedWithKey()
		{
			this.provider.Start (MessageTypes);

			IAsymmetricKey key = null;

			var test = new AsyncTest (e =>
			{
				var ce = (ConnectionMadeEventArgs)e;

				Assert.IsNotNull (ce.ClientPublicKey);
				Assert.AreEqual (key, ce.ClientPublicKey);
			});

			this.provider.ConnectionMade += test.PassHandler;

			var c = GetNewClientConnection (out key);
			if (key == null)
				Assert.Ignore();

			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void Connected()
		{
			this.provider.Start (MessageTypes);

			var c = GetNewClientConnection();

			var test = new AsyncTest();
			c.Connected += test.PassHandler;
			c.Disconnected += test.FailHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ConnectFromDisconnected()
		{
			this.provider.Start (MessageTypes);

			var wait = new ManualResetEvent (false);
			var test = new AsyncTest();

			var c = GetNewClientConnection();
			bool first = false;
			c.Connected += (s, e) =>
			{
				if (!first)
				{
					first = true;
					wait.Set();
				}
				else
				{
					test.PassHandler (s, e);
				}
			};

			EventHandler<DisconnectedEventArgs> dhandler = null;
			dhandler = (s, e) =>
			{
				c.Disconnected -= dhandler;
				c.ConnectAsync (this.EndPoint, this.MessageTypes);
			};

			c.Disconnected += dhandler;

			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			c.Disconnect();

			test.Assert (5000);
		}

		[Test]
		public void InlineSupport()
		{
			var c = GetNewClientConnection();
			
			if ((c.Modes & MessagingModes.Inline) == MessagingModes.Inline)
			{
				Assert.DoesNotThrow (() => c.Tick());
			}
			else
			{
				Assert.Throws<NotSupportedException> (() => c.Tick());
			}
		}

		[Test]
		public void ClientSendMessageInline()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Inline) != MessagingModes.Inline)
				Assert.Ignore();

			throw new NotImplementedException();
		}

		[Test]
		public void ServerSendMessageInline()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Inline) != MessagingModes.Inline)
				Assert.Ignore();

			throw new NotImplementedException();
		}

		[Test, Repeat (3)]
		public void ClientSendMessageAsync()
		{
			const string content = "Oh, hello there.";

			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			IServerConnection connection = null;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (me.Connection, connection);

				var msg = (me.Message as MockMessage);
				Assert.IsNotNull (msg);
				Assert.AreEqual (content, msg.Content);
			});

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) =>
			{
				connection = e.Connection;
				e.Connection.MessageReceived += test.PassHandler;
			};

			c.Connected += (sender, e) => ThreadPool.QueueUserWorkItem (o => c.Send (new MockMessage { Content = content }));
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ClientSendMessageConnectedHandler()
		{
			const string content = "Oh, hello there.";

			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			IServerConnection connection = null;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (me.Connection, connection);

				var msg = (me.Message as MockMessage);
				Assert.IsNotNull (msg);
				Assert.AreEqual (content, msg.Content);
			});

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) =>
			{
				connection = e.Connection;
				e.Connection.MessageReceived += test.PassHandler;
			};

			c.Connected += (sender, e) => c.Send (new MockMessage { Content = content });
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ClientMessageSent()
		{
			const string content = "Oh, hello there.";

			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();
			
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (me.Connection, c);

				var msg = (me.Message as MockMessage);
				Assert.IsNotNull (msg);
				Assert.AreEqual (content, msg.Content);
			});

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.MessageSent += test.PassHandler;
			c.Connected += (sender, e) => c.Send (new MockMessage { Content = content });
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ServerSendMessageAsync()
		{
			const string content = "Oh, hello there.";

			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (c, me.Connection);

				var msg = (me.Message as MockMessage);
				Assert.IsNotNull (msg);
				Assert.AreEqual (content, msg.Content);
			});

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => e.Connection.Send (new MockMessage { Content = content });

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void SendLongMessageAsync()
		{
			StringBuilder contentBuilder = new StringBuilder();
			Random r = new Random (42);
			for (int i = 0; i < 1000000; ++i)
				contentBuilder.Append ((char)r.Next (0, 128));

			string content = contentBuilder.ToString();

			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (c, me.Connection);

				var msg = (me.Message as MockMessage);
				Assert.IsNotNull (msg);
				Assert.AreEqual (content, msg.Content);
			});

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => e.Connection.Send (new MockMessage { Content = content });

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (30000);
		}

		[Test, Repeat (3)]
		public void Stress()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			const int messages = 1000;
			int message = 0;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull(me);
				Assert.AreSame (c, me.Connection);

				Assert.AreEqual ((message++).ToString(), ((MockMessage)me.Message).Content);

				if (message == messages)
					Assert.Pass();
			}, true);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => (new Thread (() =>
			{
				try
				{
					for (int i = 0; i < messages; ++i)
					{
						if (i > Int32.MaxValue)
							System.Diagnostics.Debugger.Break();

						e.Connection.Send (new MockMessage { Content = i.ToString() });
					}
				}
				catch (Exception ex)
				{
					test.FailWith (ex);
				}
			})).Start();

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (60000);
		}

		[Test, Repeat (3)]
		public void StressAuthenticatedAndEncrypted()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			const int messages = 1000;
			int number = 0;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (c, me.Connection);

				Assert.AreEqual (number, ((AuthenticatedAndEncryptedMessage)me.Message).Number);
				Assert.AreEqual (number.ToString(), ((AuthenticatedAndEncryptedMessage)me.Message).Message);

				if (++number == messages)
					Assert.Pass();
			}, true);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => (new Thread (() =>
			{
				try
				{
					for (int i = 0; i < messages; ++i)
					{
						if (i > Int32.MaxValue)
							System.Diagnostics.Debugger.Break();

						e.Connection.Send (new AuthenticatedAndEncryptedMessage { Number = i, Message = i.ToString() });
					}
				}
				catch (Exception ex)
				{
					test.FailWith (ex);
				}
			})).Start();

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (80000);
		}

		[Test, Repeat (3)]
		public void ConnectionFailed()
		{
			Assert.IsFalse (provider.IsRunning);

			var test = new AsyncTest();
			var c = GetNewClientConnection();
			c.Connected += test.FailHandler;
			c.Disconnected += (s, e) =>
			{
				if (e.Result == ConnectionResult.ConnectionFailed)
					test.PassHandler (s, e);
				else
					test.FailHandler (s, e);
			};

			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (30000);
		}

		[Test, Repeat (3)]
		public void StressRandomLongAuthenticatedMessage()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			const int messages = 1000;
			int number = 0;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (c, me.Connection);

				Assert.AreEqual (number, ((AuthenticatedMessage)me.Message).Number);
				Assert.IsTrue ((((AuthenticatedMessage)me.Message).Message.Length >= 7500));

				if (++number == messages)
					Assert.Pass();
			}, messages);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => (new Thread (() =>
			{
				try
				{
					var r = new Random (42);
					for (int i = 0; i < messages; ++i)
					{
						if (i > Int32.MaxValue)
							System.Diagnostics.Debugger.Break();

						e.Connection.Send (new AuthenticatedMessage { Number = i, Message = GetLongString (r.Next(7500, 100000)) });
					}
				}
				catch (Exception ex)
				{
					test.FailWith (ex);
				}
			})).Start();

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (900000);
		}

		[Test, Repeat (3)]
		public void StressRandomLongTypeHeaderedAuthenticatedMessage()
		{
			var c = GetNewClientConnection();
			if ((c.Modes & MessagingModes.Async) != MessagingModes.Async)
				Assert.Ignore();

			const int messages = 1000;

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);
				Assert.AreSame (c, me.Connection);
			}, messages);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => (new Thread (() =>
			{
				try
				{
					var r = new Random (42);
					for (int i = 0; i < messages; ++i)
					{
						if (i > Int32.MaxValue)
							System.Diagnostics.Debugger.Break();

						e.Connection.Send (new AuthenticatedTypeHeaderedMessage { Object = GetLongString (r.Next (7500, 100000)) });
					}
				}
				catch (Exception ex)
				{
					test.FailWith (ex);
				}
			})).Start();

			c.Disconnected += test.FailHandler;
			c.MessageReceived += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (900000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromClientOnClient()
		{
			this.provider.Start (MessageTypes);

			var test = new AsyncTest (e =>
			{
				var args = (DisconnectedEventArgs)e;
				switch (args.Result)
				{
					case ConnectionResult.ConnectionFailed:
						return false;

					default:
						return true;
				}
			});

			var c = GetNewClientConnection();

			var wait = new AutoResetEvent (false);

			c.Disconnected += test.PassHandler;
			c.Connected += (sender, e) => wait.Set();
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");
			
			c.Disconnect();

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromClientOnClientInConnectedHandler()
		{
			this.provider.Start (MessageTypes);

			var test = new AsyncTest (e =>
			{
				var args = (DisconnectedEventArgs)e;
				switch (args.Result)
				{
					case ConnectionResult.ConnectionFailed:
						return false;

					default:
						return true;
				}
			});

			var c = GetNewClientConnection();

			c.Disconnected += test.PassHandler;
			c.Connected += (sender, e) => c.Disconnect();
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromClientOnServer()
		{
			var test = new AsyncTest();

			var c = GetNewClientConnection();

			var wait = new AutoResetEvent (false);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (s, e) =>
			{
				e.Connection.Disconnected += test.PassHandler;
				wait.Set();
			};

			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne(10000))
				Assert.Fail ("Failed to connect");

			c.Disconnect();
			
			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromServerOnClient()
		{
			var test = new AsyncTest();

			var c = GetNewClientConnection();
			c.Disconnected += test.PassHandler;

			this.provider.Start (MessageTypes);

			IServerConnection sc = null;

			var wait = new AutoResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				wait.Set();
			};

			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000) || sc == null)
				Assert.Fail ("Failed to connect");

			sc.Disconnect();

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void ConnectionRejected()
		{
			var test = new AsyncTest();

			var c = GetNewClientConnection();
			c.Disconnected += test.PassHandler;

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) => e.Rejected = true;
			
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromServerOnServerWithinConnectionMade()
		{
			var test = new AsyncTest();

			var c = GetNewClientConnection();

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) =>
			{
				e.Connection.Disconnected += test.PassHandler;
				e.Connection.Disconnect();
			};
			
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectFromServerOnServer()
		{
			var test = new AsyncTest();

			var c = GetNewClientConnection();
			IServerConnection sc = null;

			var wait = new AutoResetEvent (false);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) =>
			{
				e.Connection.Disconnected += test.PassHandler;
				sc = e.Connection;
				wait.Set();
			};
			
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000) || sc == null)
				Assert.Fail ("Failed to connect");

			sc.Disconnect();

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void DisconnectAndReconnect()
		{
			var wait = new ManualResetEvent (false);

			var c = GetNewClientConnection();
			c.Connected += (sender, e) => wait.Set();
			c.Disconnected += (sender, e) => wait.Set();

			this.provider.Start (MessageTypes);

			for (int i = 0; i < 5; ++i)
			{
				Trace.WriteLine ("Connecting");
				wait.Reset();
				c.ConnectAsync (EndPoint, MessageTypes);
				if (!wait.WaitOne (10000))
					Assert.Fail ("Failed to connect. Attempt {0}.", i + 1);
				Trace.WriteLine ("Connected");

				Trace.WriteLine ("Disconnecting");
				wait.Reset();
				c.Disconnect();
				if (!wait.WaitOne (10000))
					Assert.Fail ("Failed to disconnect. Attempt {0}.", i + 1);

				Trace.WriteLine ("Disconnected");
			}
		}

		[Test, Repeat (3)]
		public void DisconnectAndReconnectAsync()
		{
			AutoResetEvent wait = new AutoResetEvent (false);

			var c = GetNewClientConnection();
			c.Connected += (sender, e) => wait.Set();
			c.Disconnected += (sender, e) => wait.Set();

			this.provider.Start (MessageTypes);

			for (int i = 0; i < 5; ++i)
			{
				c.ConnectAsync (EndPoint, MessageTypes);
				if (!wait.WaitOne (10000))
					Assert.Fail ("Failed to connect. Attempt {0}.", i);

				c.DisconnectAsync();
				if (!wait.WaitOne (10000))
					Assert.Fail ("Failed to disconnect. Attempt {0}.", i);
			}
		}

		[Test, Repeat (3)]
		public void DisconnectAyncWithReason()
		{
			var test = new AsyncTest (e => ((DisconnectedEventArgs)e).Result == ConnectionResult.IncompatibleVersion, 2);

			this.provider.Start (MessageTypes);
			this.provider.ConnectionMade += (sender, e) =>
			{
				e.Connection.Disconnected += test.PassHandler;

				e.Connection.Send (new DisconnectMessage { Reason = ConnectionResult.IncompatibleVersion });
				e.Connection.DisconnectAsync (ConnectionResult.IncompatibleVersion);
			};

			var c = GetNewClientConnection();
			c.Disconnected += test.PassHandler;
			c.ConnectAsync (EndPoint, MessageTypes);

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void EncryptedMessage()
		{
			var cmessage = new EncryptedMessage
			{
				Message = "It's a secret!",
				Number = 42
			};

			var c = GetNewClientConnection();
			
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as EncryptedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Encrypted);
				Assert.AreEqual (cmessage.Message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.MessageSent += test.PassHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void EncryptedLongMessage()
		{
			Random r = new Random();
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < 1000000; ++i)
				builder.Append ((char)r.Next (1, 20));

			string message = builder.ToString();
			var cmessage = new EncryptedMessage
			{
				Message = message,
				Number = 42
			};

			var c = GetNewClientConnection();
			
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as EncryptedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Encrypted);
				Assert.AreEqual (message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne(10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void AuthenticatedLongMessage()
		{
			var message = GetLongString();
			var cmessage = new AuthenticatedMessage
			{
				Message = message,
				Number = 42
			};

			var c = GetNewClientConnection();
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as AuthenticatedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Authenticated);
				Assert.AreEqual (cmessage.Message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.MessageSent += test.PassHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void AuthenticatedMessage()
		{
			var cmessage = new AuthenticatedMessage
			{
				Message = "It's a secret!",
				Number = 42
			};

			var c = GetNewClientConnection();
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as AuthenticatedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Authenticated);
				Assert.AreEqual (cmessage.Message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.MessageSent += test.PassHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void BlankMessage()
		{
			var cmessage = new BlankMessage();

			var c = GetNewClientConnection();
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as BlankMessage);
				Assert.IsNotNull (msg);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void EncryptedAndAuthenticatedMessage()
		{

			Random r = new Random();
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < 1000000; ++i)
				builder.Append ((char)r.Next (1, 20));

			string message = builder.ToString();
			var cmessage = new AuthenticatedAndEncryptedMessage
			{
				Message = message,
				Number = 42
			};

			var c = GetNewClientConnection();
			
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as AuthenticatedAndEncryptedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Encrypted);
				Assert.IsTrue (msg.Authenticated);
				Assert.AreEqual (cmessage.Message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void EncryptedAndAuthenticatedLongMessage()
		{
			var cmessage = new AuthenticatedAndEncryptedMessage()
			{
				Message = "It's a secret!",
				Number = 42
			};

			var c = GetNewClientConnection();
			
			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as AuthenticatedAndEncryptedMessage);
				Assert.IsNotNull (msg);
				Assert.IsTrue (msg.Encrypted);
				Assert.IsTrue (msg.Authenticated);
				Assert.AreEqual (cmessage.Message, msg.Message);
				Assert.AreEqual (cmessage.Number, msg.Number);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send (cmessage);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}

		[Test, Repeat (3)]
		public void AuthenticatedTypeHeaderedMessage()
		{
			var cmessage = new AuthenticatedTypeHeaderedMessage
			{
				Object = new ArrayList()
			};

			AssertMessageReceived (cmessage, msg =>
			{
				Assert.IsFalse (msg.Encrypted);
				Assert.IsTrue (msg.Authenticated);
				Assert.IsInstanceOf<ArrayList> (msg.Object);
			});
		}

		[Test, Repeat (3)]
		public void AuthenticatedTypeHeaderedMessageLong()
		{
			var cmessage = new AuthenticatedTypeHeaderedMessage
			{
				Object = GetLongString()
			};

			AssertMessageReceived (cmessage, msg =>
			{
				Assert.IsFalse (msg.Encrypted);
				Assert.IsTrue (msg.Authenticated);
				Assert.IsInstanceOf<string> (msg.Object);
				Assert.AreEqual (cmessage.Object, msg.Object);
			});

		}

		#if NET_4
		[Test, Repeat (3)]
		public void SendAndRespond()
		{
			var cmessage = new MockMessage();
			cmessage.Content = "Blah";

			var c = GetNewClientConnection();
			
			var test = new AsyncTest<MessageEventArgs<MockMessage>>  (e =>
			{
				Assert.IsNotNull (e.Message);
				Assert.AreEqual ("Response", e.Message.Content);
			});

			IConnection sc = null;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += (ms, me) =>
				{
					var msg = (me.Message as MockMessage);
					me.Connection.SendResponse (msg, new MockMessage { Content = "Response" });
				};
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send<MockMessage> (cmessage).ContinueWith (t => test.PassHandler (sc, new MessageEventArgs<MockMessage> (sc, t.Result)));
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}
		#endif

		private static string GetLongString (int length = 1000000)
		{
			Random r = new Random();
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < length; ++i)
				builder.Append ((char)r.Next (1, 20));

			return builder.ToString();
		}

		private void AssertMessageReceived<T> (T message, Action<T> testResults)
			where T : Message
		{
			var c = GetNewClientConnection();

			var test = new AsyncTest (e =>
			{
				var me = (e as MessageEventArgs);
				Assert.IsNotNull (me);

				var msg = (me.Message as T);
				Assert.IsNotNull (msg);
				testResults (msg);
			});

			IConnection sc;
			ManualResetEvent wait = new ManualResetEvent (false);
			this.provider.ConnectionMade += (s, e) =>
			{
				sc = e.Connection;
				sc.MessageReceived += test.PassHandler;
				sc.Disconnected += test.FailHandler;
				wait.Set();
			};

			this.provider.Start (MessageTypes);

			c.Disconnected += test.FailHandler;
			c.Connected += (sender, e) => c.Send (message);
			c.ConnectAsync (EndPoint, MessageTypes);

			if (!wait.WaitOne (10000))
				Assert.Fail ("Failed to connect");

			test.Assert (10000);
		}
	}
}