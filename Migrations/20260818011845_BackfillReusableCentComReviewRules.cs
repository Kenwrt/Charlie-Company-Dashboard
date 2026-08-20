using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CharleyCompany.Dashboard.Web.Migrations
{
    /// <inheritdoc />
    public partial class BackfillReusableCentComReviewRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "CentComResolutionRules"
                    ("TaskType", "RuleKind", "MatchText", "ReviewStatus", "ResolutionAction",
                     "EstimatorResponse", "VendorProductId", "CreatedByUserId", "CreatedAt", "IsActive")
                SELECT task."TaskType",
                       CASE WHEN review."ReviewKind" = 'Assumption' THEN 'Assumption' ELSE 'Warning' END,
                       review."Description", review."Status", review."ResolutionAction",
                       review."EstimatorResponse", review."AddedVendorProductId", review."ResolvedByUserId",
                       COALESCE(review."ResolvedAt", analysis."CompletedAt", NOW()), TRUE
                FROM "QuoteTaskAnalysisReviewItems" review
                INNER JOIN "QuoteTaskAnalyses" analysis ON analysis."Id" = review."QuoteTaskAnalysisId"
                INNER JOIN "QuoteProjectTasks" task ON task."Id" = analysis."QuoteProjectTaskId"
                WHERE review."Status" IN ('Accepted', 'Resolved', 'Not applicable')
                ON CONFLICT ("TaskType", "RuleKind", "MatchText") DO NOTHING;

                INSERT INTO "CentComResolutionRules"
                    ("TaskType", "RuleKind", "MatchText", "MaterialDecision", "VendorProductId",
                     "MaterialDescription", "MaterialUnit", "MaterialUnitCost", "CreatedAt", "IsActive")
                SELECT task."TaskType", 'Material', COALESCE(material."OriginalDescription", material."Description"),
                       material."ReviewDecision", material."VendorProductId", material."Description",
                       material."Unit", material."UnitCost", COALESCE(analysis."CompletedAt", NOW()), TRUE
                FROM "QuoteTaskAnalysisMaterials" material
                INNER JOIN "QuoteTaskAnalyses" analysis ON analysis."Id" = material."QuoteTaskAnalysisId"
                INNER JOIN "QuoteProjectTasks" task ON task."Id" = analysis."QuoteProjectTaskId"
                WHERE material."ReviewDecision" IN ('Accepted', 'Replaced', 'Removed')
                  AND (
                    (material."MatchKind" = 'One-off' AND material."SourceType" IN ('Estimator accepted one-off', 'Manual one-off price'))
                    OR (material."ReviewDecision" = 'Removed' AND material."MatchKind" = 'Unresolved')
                    OR (material."ReviewDecision" = 'Replaced' AND material."IsEstimatorLocked" = TRUE)
                  )
                ON CONFLICT ("TaskType", "RuleKind", "MatchText") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "CentComResolutionRules" rule
                WHERE EXISTS (
                    SELECT 1
                    FROM "QuoteTaskAnalysisReviewItems" review
                    INNER JOIN "QuoteTaskAnalyses" analysis ON analysis."Id" = review."QuoteTaskAnalysisId"
                    INNER JOIN "QuoteProjectTasks" task ON task."Id" = analysis."QuoteProjectTaskId"
                    WHERE task."TaskType" = rule."TaskType"
                      AND rule."RuleKind" = CASE WHEN review."ReviewKind" = 'Assumption' THEN 'Assumption' ELSE 'Warning' END
                      AND review."Description" = rule."MatchText"
                );
                """);
        }
    }
}
