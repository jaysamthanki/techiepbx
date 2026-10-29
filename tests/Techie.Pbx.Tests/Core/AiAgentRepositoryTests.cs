using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// AI agent validation that needs the database (D199): the number has to be free from either
    /// side, a handoff has to be a live destination other than an agent, and a renumber takes references with it.
    /// </summary>
    public class AiAgentRepositoryTests : IDisposable
    {
        private readonly AiAgentRepository agents;
        private readonly Database database;
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-agents-").FullName;
        private readonly ExtensionRepository extensions;

        public AiAgentRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.agents = new AiAgentRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private static AiAgent Agent(string number = "7300", string transferTo = "") => new()
        {
            Number = number,
            Name = "Reception",
            Prompt = "You are the receptionist.",
            TransferTo = transferTo,
        };

        private long AddAnnouncement(string playExtension) =>
            new AnnouncementRepository(this.database).Insert(new Announcement
            {
                Name = "Greeting " + playExtension,
                AudioFile = "greeting.wav",
                PlayExtension = playExtension,
            });

        private void AddExtension(string number, bool enabled = true) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
                Enabled = enabled,
            });

        private static ValidationFailedException Refused(Action action) =>
            Assert.Throws<ValidationFailedException>(action);

        [Fact]
        public void Insert_then_read_back()
        {
            AddExtension("1001");

            var agent = Agent(transferTo: "Extension:1001");
            agent.Greeting = "Hello.";
            agent.VoiceId = "voice_1";
            agent.Model = "vendor/model";

            var id = this.agents.Insert(agent);
            var loaded = this.agents.GetByID(id)!;

            Assert.Equal("7300", loaded.Number);
            Assert.Equal("Hello.", loaded.Greeting);
            Assert.Equal("voice_1", loaded.VoiceId);
            Assert.Equal("vendor/model", loaded.Model);
            Assert.Equal("Extension:1001", loaded.TransferTo);
            Assert.True(loaded.Enabled);
            Assert.Equal(loaded.ServiceUuid, this.agents.GetByNumber("7300")!.ServiceUuid);
        }

        [Fact]
        public void An_agent_cannot_take_an_extension_number()
        {
            AddExtension("1001");

            var ex = Refused(() => this.agents.Insert(Agent("1001")));
            Assert.Contains("Extension 1001 already uses that number.", ex.Errors);
        }

        [Fact]
        public void An_agent_cannot_take_a_ring_group_number_and_the_reverse()
        {
            AddExtension("1001");
            var groups = new RingGroupRepository(this.database);
            groups.Insert(new RingGroup { Number = "600", Name = "Support", Members = "1001", DestinationType = "Hangup" });

            Assert.Contains("Ring group 600 already uses that number.", Refused(() => this.agents.Insert(Agent("600"))).Errors);

            this.agents.Insert(Agent("601"));
            Assert.Contains("AI agent 'Reception' already uses 601.",
                Refused(() => groups.Insert(new RingGroup { Number = "601", Name = "Sales", Members = "1001", DestinationType = "Hangup" })).Errors);
        }

        [Fact]
        public void An_agent_cannot_take_an_announcement_number_and_the_reverse()
        {
            AddAnnouncement("7100");

            Assert.Contains("Announcement 'Greeting 7100' already plays on 7100.",
                Refused(() => this.agents.Insert(Agent("7100"))).Errors);

            this.agents.Insert(Agent("7101"));
            Assert.Contains("AI agent 'Reception' already uses 7101.", Refused(() => AddAnnouncement("7101")).Errors);
        }

        [Fact]
        public void An_agent_cannot_take_an_ivr_number_and_the_reverse()
        {
            var greeting = AddAnnouncement("7100");
            var ivrs = new IvrRepository(this.database);
            ivrs.Insert(new Ivr { Name = "Main menu", AnnouncementID = greeting, PlayExtension = "7200" });

            Assert.Contains("IVR 'Main menu' already plays on 7200.", Refused(() => this.agents.Insert(Agent("7200"))).Errors);

            this.agents.Insert(Agent("7201"));
            Assert.Contains("AI agent 'Reception' already uses 7201.",
                Refused(() => ivrs.Insert(new Ivr { Name = "Second menu", AnnouncementID = greeting, PlayExtension = "7201" })).Errors);
        }

        [Fact]
        public void An_agent_cannot_take_a_time_condition_number_and_the_reverse()
        {
            var conditions = new TimeConditionRepository(this.database);
            conditions.Insert(new TimeCondition { Name = "Office hours", PlayExtension = "7400" });

            Assert.Contains("Time condition 'Office hours' already uses 7400.", Refused(() => this.agents.Insert(Agent("7400"))).Errors);

            this.agents.Insert(Agent("7401"));
            Assert.Contains("AI agent 'Reception' already uses 7401.",
                Refused(() => conditions.Insert(new TimeCondition { Name = "Weekend", PlayExtension = "7401" })).Errors);
        }

        [Fact]
        public void Two_agents_cannot_share_a_number()
        {
            this.agents.Insert(Agent("7300"));

            Assert.NotEmpty(Refused(() => this.agents.Insert(Agent("7300"))).Errors);
        }

        private const string Gone = "That transfer destination is not there any more. Choose another.";

        private long AddRingGroup(string number, bool enabled = true) =>
            new RingGroupRepository(this.database).Insert(new RingGroup
            {
                Number = number,
                Name = "Front desk",
                Members = "1001",
                DestinationType = "Hangup",
                Enabled = enabled,
            });

        [Fact]
        public void The_transfer_can_be_a_ring_group()
        {
            AddExtension("1001");
            AddRingGroup("600");

            var id = this.agents.Insert(Agent(transferTo: "RingGroup:600"));

            Assert.Equal("RingGroup:600", this.agents.GetByID(id)!.ToTransferDestination()!.Key);
        }

        [Fact]
        public void The_transfer_cannot_be_another_agent()
        {
            this.agents.Insert(Agent("7301"));

            Assert.Contains("An agent cannot transfer a call to an AI agent.",
                Refused(() => this.agents.Insert(Agent(transferTo: "AiAgent:7301"))).Errors);
        }

        [Fact]
        public void The_transfer_target_has_to_exist()
        {
            Assert.Contains(Gone, Refused(() => this.agents.Insert(Agent(transferTo: "Extension:1009"))).Errors);
            Assert.Contains(Gone, Refused(() => this.agents.Insert(Agent(transferTo: "RingGroup:609"))).Errors);
        }

        [Fact]
        public void The_transfer_target_has_to_be_enabled()
        {
            AddExtension("1001");
            AddExtension("1002", enabled: false);
            AddRingGroup("600", enabled: false);

            Assert.Contains(Gone, Refused(() => this.agents.Insert(Agent(transferTo: "Extension:1002"))).Errors);
            Assert.Contains(Gone, Refused(() => this.agents.Insert(Agent(transferTo: "RingGroup:600"))).Errors);
        }

        [Fact]
        public void Renumbering_the_transfer_target_takes_the_handoff_with_it()
        {
            AddExtension("1001");
            var groups = new RingGroupRepository(this.database);
            var groupID = AddRingGroup("600");
            var id = this.agents.Insert(Agent(transferTo: "RingGroup:600"));

            var group = groups.GetByID(groupID)!;
            group.Number = "610";
            groups.Update(group);

            Assert.Equal("RingGroup:610", this.agents.GetByID(id)!.TransferTo);
        }

        [Fact]
        public void Renumbering_an_agent_takes_its_references_with_it()
        {
            var id = this.agents.Insert(Agent("7300"));
            var conditions = new TimeConditionRepository(this.database);

            // Through the repository: a time condition may now point at a live agent (D199).
            var conditionID = conditions.Insert(new TimeCondition
            {
                Name = "Office hours",
                PlayExtension = "7400",
                OpenDestinationType = "AiAgent",
                OpenDestinationValue = "7300",
            });

            var agent = this.agents.GetByID(id)!;
            var uuid = agent.ServiceUuid;
            agent.Number = "7310";
            this.agents.Update(agent);

            Assert.Equal("AiAgent:7310", conditions.GetByID(conditionID)!.ToOpenDestination().Key);
            Assert.Equal(uuid, this.agents.GetByID(id)!.ServiceUuid);
        }

        private long AddTrunk() =>
            new TrunkRepository(this.database).Insert(new Trunk
            {
                Name = "callcentric",
                ServerHost = "callcentric.com",
                Username = "17771234567",
                Password = "not-a-real-password",
                Register = true,
            });

        private static InboundRoute RouteTo(long trunkID, string agentNumber) => new()
        {
            TrunkID = trunkID,
            DID = "17771234567",
            DestinationType = "AiAgent",
            DestinationValue = agentNumber,
            Description = "Main line",
        };

        /// <summary>The repositories now see agents, so a route to a live one saves (D199).</summary>
        [Fact]
        public void An_inbound_route_can_point_at_a_live_agent()
        {
            this.agents.Insert(Agent("7300"));
            var routes = new InboundRouteRepository(this.database);

            var id = routes.Insert(RouteTo(AddTrunk(), "7300"));

            Assert.Equal("AiAgent:7300", routes.GetByID(id)!.ToDestination().Key);
        }

        [Fact]
        public void An_inbound_route_cannot_point_at_a_disabled_or_missing_agent()
        {
            var agent = Agent("7300");
            agent.Enabled = false;
            this.agents.Insert(agent);
            var routes = new InboundRouteRepository(this.database);
            var trunkID = AddTrunk();

            Assert.Contains("That destination is not there any more. Choose another.",
                Refused(() => routes.Insert(RouteTo(trunkID, "7300"))).Errors);
            Assert.Contains("That destination is not there any more. Choose another.",
                Refused(() => routes.Insert(RouteTo(trunkID, "7399"))).Errors);
        }

        [Fact]
        public void A_ring_group_can_fail_over_to_an_agent()
        {
            AddExtension("1001");
            this.agents.Insert(Agent("7300"));

            new RingGroupRepository(this.database).Insert(new RingGroup
            {
                Number = "600",
                Name = "Support",
                Members = "1001",
                DestinationType = "AiAgent",
                DestinationValue = "7300",
            });
        }

        [Fact]
        public void Delete_removes_the_row()
        {
            var id = this.agents.Insert(Agent());

            this.agents.Delete(id);

            Assert.Null(this.agents.GetByID(id));
        }

        [Fact]
        public void An_agent_is_found_by_its_socket_uuid_only_while_it_is_enabled()
        {
            var agent = Agent();
            this.agents.Insert(agent);

            Assert.Equal(agent.AiAgentID, this.agents.GetByServiceUuid(agent.ServiceUuid)?.AiAgentID);
            Assert.Null(this.agents.GetByServiceUuid(Guid.NewGuid().ToString("D")));

            agent.Enabled = false;
            this.agents.Update(agent);

            Assert.Null(this.agents.GetByServiceUuid(agent.ServiceUuid));
        }
    }
}
