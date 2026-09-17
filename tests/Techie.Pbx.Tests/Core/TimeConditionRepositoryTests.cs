using Microsoft.Data.Sqlite;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Core
{
    /// <summary>
    /// Time condition validation: the play extension shares a number space with everything else
    /// that is dialled, and the rules have to survive being written into a GotoIfTime (D63, D64).
    /// </summary>
    public class TimeConditionRepositoryTests : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-time-conditions-").FullName;
        private readonly Database database;
        private readonly TimeConditionRepository conditions;
        private readonly ExtensionRepository extensions;

        public TimeConditionRepositoryTests()
        {
            this.database = new Database(Path.Combine(this.directory, "tnpbx.db"));
            this.database.Migrate();
            this.conditions = new TimeConditionRepository(this.database);
            this.extensions = new ExtensionRepository(this.database);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(this.directory, recursive: true);
        }

        private static TimeCondition Condition(string name = "Office hours", string playExtension = "600") => new()
        {
            Name = name,
            Description = "Nine to five",
            PlayExtension = playExtension,
            Rules = new List<TimeConditionRule>
            {
                new()
                {
                    Kind = TimeConditionRuleKind.Weekly,
                    DaysMask = TimeConditionRule.Monday | TimeConditionRule.Tuesday | TimeConditionRule.Wednesday |
                        TimeConditionRule.Thursday | TimeConditionRule.Friday,
                    StartTime = "09:00",
                    EndTime = "17:00",
                },
            },
        };

        private void AddExtension(string number) =>
            this.extensions.Insert(new Extension
            {
                Number = number,
                Name = "Phone " + number,
                Secret = "AAAAbbbbCCCCdddd1111",
            });

        [Fact]
        public void Insert_then_read_back_with_rules()
        {
            var id = this.conditions.Insert(Condition());
            var loaded = this.conditions.GetByID(id)!;

            Assert.Equal("Office hours", loaded.Name);
            Assert.Equal("600", loaded.PlayExtension);
            var rule = Assert.Single(loaded.Rules);
            Assert.Equal(TimeConditionRuleKind.Weekly, rule.Kind);
            Assert.Equal("09:00", rule.StartTime);
            Assert.Equal("17:00", rule.EndTime);
        }

        [Fact]
        public void Update_replaces_the_rules_wholesale()
        {
            var id = this.conditions.Insert(Condition());
            var loaded = this.conditions.GetByID(id)!;

            loaded.Rules =
            [
                new TimeConditionRule
                {
                    Kind = TimeConditionRuleKind.Holiday,
                    HolidayDate = "2026-12-25",
                },
            ];

            this.conditions.Update(loaded);

            var reread = this.conditions.GetByID(id)!;
            var rule = Assert.Single(reread.Rules);
            Assert.Equal(TimeConditionRuleKind.Holiday, rule.Kind);
            Assert.Equal("2026-12-25", rule.HolidayDate);
        }

        [Fact]
        public void Duplicate_name_is_refused()
        {
            this.conditions.Insert(Condition());

            Assert.Throws<ValidationFailedException>(() => this.conditions.Insert(Condition()));
        }

        [Fact]
        public void Play_extension_cannot_collide_with_an_extension()
        {
            this.AddExtension("600");

            Assert.Throws<ValidationFailedException>(() => this.conditions.Insert(Condition()));
        }

        [Fact]
        public void Two_holidays_on_the_same_day_of_the_year_are_refused()
        {
            var condition = Condition();
            condition.Rules =
            [
                new TimeConditionRule { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2026-12-25" },
                new TimeConditionRule { Kind = TimeConditionRuleKind.Holiday, HolidayDate = "2027-12-25" },
            ];

            Assert.Throws<ValidationFailedException>(() => this.conditions.Insert(condition));
        }

        [Fact]
        public void Delete_takes_the_rules_with_it()
        {
            var id = this.conditions.Insert(Condition());

            this.conditions.Delete(id);

            Assert.Null(this.conditions.GetByID(id));
            Assert.Empty(this.conditions.GetAll());
        }
    }
}
