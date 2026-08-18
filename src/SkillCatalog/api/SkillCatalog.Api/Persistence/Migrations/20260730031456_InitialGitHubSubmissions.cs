using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SkillCatalog.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialGitHubSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditTransitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorType = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: true),
                    FromState = table.Column<int>(type: "integer", nullable: false),
                    ToState = table.Column<int>(type: "integer", nullable: false),
                    ReasonCode = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GitHubResourceId = table.Column<string>(type: "text", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditTransitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizationTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StateDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PkceVerifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OpenerOrigin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Contributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionIntentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributorGitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    ForkOwner = table.Column<string>(type: "text", nullable: false),
                    ForkRepository = table.Column<string>(type: "text", nullable: false),
                    BranchName = table.Column<string>(type: "text", nullable: false),
                    CommitSha = table.Column<string>(type: "text", nullable: true),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: true),
                    PullRequestUrl = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastCompletedStep = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureCategory = table.Column<string>(type: "text", nullable: true),
                    RecoveryMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributorSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    GitHubLogin = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: true),
                    AccessExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributorSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyLeases",
                columns: table => new
                {
                    ContributorGitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    SubmissionIntentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContributionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseOwner = table.Column<string>(type: "text", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyLeases", x => new { x.ContributorGitHubUserId, x.IdempotencyKey });
                });

            migrationBuilder.CreateTable(
                name: "SubmissionIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributorSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationRevision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContributionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetRepository = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BaseBranch = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PluginId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SkillId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DestinationPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    BaseCommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PullRequestTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PullRequestBody = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    FileManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionIntents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    DeliveryId = table.Column<string>(type: "text", nullable: false),
                    EventName = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContributionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadDigest = table.Column<string>(type: "text", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.DeliveryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditTransitions_ContributionId_OccurredAt",
                table: "AuditTransitions",
                columns: new[] { "ContributionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditTransitions_OccurredAt",
                table: "AuditTransitions",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationTransactions_ExpiresAt",
                table: "AuthorizationTransactions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationTransactions_StateDigest",
                table: "AuthorizationTransactions",
                column: "StateDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_ContributorGitHubUserId_UpdatedAt",
                table: "Contributions",
                columns: new[] { "ContributorGitHubUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_SubmissionIntentId",
                table: "Contributions",
                column: "SubmissionIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributorSessions_AccessExpiresAt",
                table: "ContributorSessions",
                column: "AccessExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContributorSessions_GitHubUserId_RevokedAt",
                table: "ContributorSessions",
                columns: new[] { "GitHubUserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyLeases_LeaseExpiresAt",
                table: "IdempotencyLeases",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionIntents_ContributorSessionId_IdempotencyKey",
                table: "SubmissionIntents",
                columns: new[] { "ContributorSessionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionIntents_ExpiresAt",
                table: "SubmissionIntents",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_ReceivedAt",
                table: "WebhookDeliveries",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditTransitions");

            migrationBuilder.DropTable(
                name: "AuthorizationTransactions");

            migrationBuilder.DropTable(
                name: "Contributions");

            migrationBuilder.DropTable(
                name: "ContributorSessions");

            migrationBuilder.DropTable(
                name: "IdempotencyLeases");

            migrationBuilder.DropTable(
                name: "SubmissionIntents");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");
        }
    }
}
