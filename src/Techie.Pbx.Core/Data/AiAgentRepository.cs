using Dapper;
using Microsoft.Data.Sqlite;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Data
{
    public class AiAgentRepository
    {
        private const string Columns =
            "AiAgentID, Number, Name, Prompt, Greeting, VoiceId, Model, TransferTo, TransferLine, Enabled";

        private const int SqliteConstraintError = 19;

        private readonly Database database;
        private readonly ConfigPendingMarker pending;

        public AiAgentRepository(Database database)
        {
            this.database = database;
            this.pending = new ConfigPendingMarker(database);
        }

        public void Delete(long aiAgentID)
        {
            using var connection = this.database.Open();
            connection.Execute("DELETE FROM AiAgents WHERE AiAgentID = @aiAgentID", new { aiAgentID });

            this.pending.Raise();
        }

        public List<AiAgent> GetAll()
        {
            using var connection = this.database.Open();
            return connection.Query<AiAgent>($"SELECT {Columns} FROM AiAgents ORDER BY CAST(Number AS INTEGER)").ToList();
        }

        public AiAgent? GetByID(long aiAgentID)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<AiAgent>(
                $"SELECT {Columns} FROM AiAgents WHERE AiAgentID = @aiAgentID", new { aiAgentID });
        }

        public AiAgent? GetByNumber(string number)
        {
            using var connection = this.database.Open();
            return connection.QuerySingleOrDefault<AiAgent>(
                $"SELECT {Columns} FROM AiAgents WHERE Number = @number", new { number });
        }

        /// <summary>
        /// The enabled agent whose socket UUID this is (D199, D200), or null. The UUID is derived
        /// from the row ID rather than stored, so this compares against every row; a site has a
        /// handful of agents, and the lookup happens once per call.
        /// </summary>
        public AiAgent? GetByServiceUuid(string serviceUuid) =>
            this.GetAll().FirstOrDefault(a =>
                a.Enabled && string.Equals(a.ServiceUuid, serviceUuid, StringComparison.OrdinalIgnoreCase));

        public long Insert(AiAgent agent)
        {
            this.ThrowIfInvalid(agent);

            using var connection = this.database.Open();
            try
            {
                agent.AiAgentID = connection.ExecuteScalar<long>(
                    "INSERT INTO AiAgents (Number, Name, Prompt, Greeting, VoiceId, Model, TransferTo, TransferLine, Enabled) " +
                    "VALUES (@Number, @Name, @Prompt, @Greeting, @VoiceId, @Model, @TransferTo, @TransferLine, @Enabled); " +
                    "SELECT last_insert_rowid();",
                    agent);

                this.pending.Raise();
                return agent.AiAgentID;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"An AI agent on {agent.Number} already exists.");
            }
        }

        /// <summary>
        /// Saves the agent. A changed number takes every reference to the old one with it, in the
        /// same transaction (piece 37), exactly as a ring group's does: the references are rewritten
        /// first, so a number another agent already has fails on the row's own UNIQUE after them
        /// and rolls the lot back. The socket UUID is derived from the row ID, not the number, so
        /// the media service still knows the agent afterwards (D199).
        /// </summary>
        public void Update(AiAgent agent)
        {
            var stored = this.GetByID(agent.AiAgentID);

            this.ThrowIfInvalid(agent);

            using var connection = this.database.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                if (stored != null && !string.Equals(stored.Number, agent.Number, StringComparison.Ordinal))
                {
                    Renumbering.Destinations(connection, transaction,
                        new Destination(DestinationType.AiAgent, stored.Number),
                        new Destination(DestinationType.AiAgent, agent.Number));
                }

                var rows = connection.Execute(
                    "UPDATE AiAgents SET Number = @Number, Name = @Name, Prompt = @Prompt, Greeting = @Greeting, " +
                    "VoiceId = @VoiceId, Model = @Model, TransferTo = @TransferTo, TransferLine = @TransferLine, Enabled = @Enabled " +
                    "WHERE AiAgentID = @AiAgentID",
                    agent, transaction);
                if (rows == 0)
                    throw new ValidationFailedException($"AiAgentID {agent.AiAgentID} does not exist.");

                transaction.Commit();
                this.pending.Raise();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintError)
            {
                throw new ValidationFailedException($"An AI agent on {agent.Number} already exists.");
            }
        }

        /// <summary>
        /// The model's own rules, plus the ones that need the database: the number is dialled out
        /// of the same context as everything else, so nothing else may have claimed it (D57) — the
        /// other repositories check the same in the other direction — and a handoff has to be a live
        /// destination, the same check every other entity's destination gets, or the call would
        /// continue to something nothing answers (D201).
        /// </summary>
        private void ThrowIfInvalid(AiAgent agent)
        {
            var errors = agent.Validate();
            var number = agent.Number;
            var extensions = new ExtensionRepository(this.database).GetAll();

            if (extensions.Any(e => string.Equals(e.Number, number, StringComparison.Ordinal)))
                errors.Add($"Extension {number} already uses that number.");

            var ringGroups = new RingGroupRepository(this.database).GetAll();

            if (ringGroups.Any(g => string.Equals(g.Number, number, StringComparison.Ordinal)))
                errors.Add($"Ring group {number} already uses that number.");

            var announcements = new AnnouncementRepository(this.database).GetAll();
            var announcement = announcements.FirstOrDefault(a => string.Equals(a.PlayExtension, number, StringComparison.Ordinal));

            if (announcement != null)
                errors.Add($"Announcement '{announcement.Name}' already plays on {number}.");

            var ivrs = new IvrRepository(this.database).GetAll();
            var ivr = ivrs.FirstOrDefault(i => string.Equals(i.PlayExtension, number, StringComparison.Ordinal));

            if (ivr != null)
                errors.Add($"IVR '{ivr.Name}' already plays on {number}.");

            var conditions = new TimeConditionRepository(this.database).GetAll();
            var condition = conditions.FirstOrDefault(t => string.Equals(t.PlayExtension, number, StringComparison.Ordinal));

            if (condition != null)
                errors.Add($"Time condition '{condition.Name}' already uses {number}.");

            var agents = this.GetAll();
            var clash = agents.FirstOrDefault(a =>
                a.AiAgentID != agent.AiAgentID && string.Equals(a.Number, number, StringComparison.Ordinal));

            if (clash != null)
                errors.Add($"AI agent '{clash.Name}' already uses {number}.");

            // A handoff that does not parse, or names an agent, is already refused by Validate.
            var target = agent.ToTransferDestination();

            if (target != null && target.Type != DestinationType.AiAgent)
            {
                var controls = new CallFlowControlRepository(this.database).GetAll();

                if (DestinationCatalog.Find(extensions, ringGroups, announcements, ivrs, conditions, controls, agents, target) == null)
                    errors.Add("That transfer destination is not there any more. Choose another.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);
        }
    }
}
