using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Weymela.Application;
using Weymela.Application.Web;
using Weymela.Domain;
using Weymela.Infrastructure.Persistence;
using Weymela.Infrastructure.Persistence.Records;
using Weymela.Infrastructure.Persistence.Repositories;
using Weymela.Infrastructure.Persistence.Transactions;

namespace Weymela.Infrastructure.Finance;

public sealed class UgcService(WeymelaDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private EfUnitOfWork Transaction => new(db, IsolationLevel.Serializable);

    public Task<Guid> CreateAsync(Actor actor, CreateUgcInput input, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(JsonSerializer.Serialize(input));
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "CreateUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            if (!Enum.TryParse<UgcContentType>(input.ContentType, true, out var contentType) || !Enum.IsDefined(contentType))
                throw new ApplicationFailure(FailureKind.Validation, "Choose Video or Photos.");
            var resources = Resources(input.Resources);
            var requirements = Requirements(input.PlatformRequirements);
            var config = await new FinancialConfigurationResolver(db).EffectiveAsync(Now, token);
            var pricing = config.Ugc
                ?? throw new InvalidOperationException("The effective financial configuration does not include UGC settings.");
            var opportunity = new UgcOpportunity(actor.BusinessId!.Value, input.Title, input.Slogan, contentType,
                input.Instructions, JsonSerializer.Serialize(resources), input.Location, input.DueDateUtc,
                input.ProductProvided, input.CreatorMustPurchase, input.UsageRights, Amount(input.CreatorPayment),
                input.CreatorsNeeded, pricing, Now, requirements);
            db.UgcOpportunities.Add(opportunity);
            db.UgcRevisions.Add(new(opportunity.Id, 1, false, Snapshot(opportunity), actor.UserId, Now));
            operation.Remember(actor, "CreateUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcCreated", opportunity.Id, null, Guid.NewGuid(), new { opportunity.Id, opportunity.Title });
            return opportunity.Id;
        }, ct);

    public Task<Guid> PublishAsync(Actor actor, Guid id, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "PublishUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token);
            Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
            var correlation = Guid.NewGuid();
            try { opportunity.Publish(wallet, Now, correlation); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("negative", StringComparison.OrdinalIgnoreCase))
            { throw new ApplicationFailure(FailureKind.InsufficientFunds, "Available balance is insufficient to publish this UGC opportunity.", ex); }
            var journal = Journal(actor, JournalSourceType.UgcReservation, opportunity.RequiredFunding,
                "BusinessAvailable", "UgcAllocatedReserve", key, correlation, opportunity.Id, null);
            db.UgcReservations.Add(new(opportunity.Id, opportunity.BusinessId, opportunity.RequiredFunding, journal.Id, Now));
            db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, opportunity.RequiredFunding, "UgcReserve", journal.Id, Now, opportunity.Id));
            db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, opportunity.RequiredFunding, "Reserved", journal.Id, Now));
            operation.Remember(actor, "PublishUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcPublished", opportunity.Id, null, correlation,
                new { opportunity.Id, opportunity.BusinessId, RequiredFunding = opportunity.RequiredFunding.Amount });
            return opportunity.Id;
        }, ct);

    public Task<Guid> RequestAsync(Actor actor, Guid id, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), actor.CreatorId!.Value.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "RequestUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token);
            if (opportunity.Status != UgcOpportunityStatus.Open || opportunity.DueDateUtc <= Now || opportunity.ApprovedCreatorCount >= opportunity.CreatorCapacity)
                throw new ApplicationFailure(FailureKind.Validation, "This UGC opportunity is not accepting requests.");
            if (!await db.CommercePermissions.AsNoTracking().AnyAsync(x => x.Role == ActorRole.Business
                    && x.SubjectId == opportunity.BusinessId && x.IsActive, token))
                throw new ApplicationFailure(FailureKind.Validation, "This UGC opportunity is not accepting requests.");
            await EnsureCreatorEligibility(actor.CreatorId.Value, opportunity, token);
            if (await db.UgcCreatorRequests.AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId
                    && (x.Status == UgcRequestStatus.Pending || x.Status == UgcRequestStatus.Approved), token))
                throw new ApplicationFailure(FailureKind.Validation, "You already requested this UGC opportunity.");
            var request = new UgcCreatorRequest(id, actor.CreatorId.Value, Now);
            db.UgcCreatorRequests.Add(request);
            operation.Remember(actor, "RequestUgc", key, fingerprint, request.Id.ToString(), Now);
            Record(actor, "UgcRequestReceived", id, actor.CreatorId, Guid.NewGuid(),
                new { OpportunityId = id, RequestId = request.Id, opportunity.BusinessId, CreatorId = actor.CreatorId.Value });
            return request.Id;
        }, ct);

    public Task<Guid> ReviewRequestAsync(Actor actor, Guid requestId, bool approve, string? reason, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(requestId.ToString(), approve.ToString(), reason?.Trim() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewUgcRequest", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var request = await db.UgcCreatorRequests.SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC request not found.");
            var opportunity = await Opportunity(request.UgcOpportunityId, token); Own(actor, opportunity);
            Guid result;
            if (approve)
            {
                opportunity.ApproveCreator(); request.Approve(actor.UserId, Now);
                var assignment = new UgcAssignment(opportunity.Id, request.Id, request.CreatorId,
                    opportunity.CreatorPayment, opportunity.PerAssignmentFee, opportunity.CurrentRevision, Now);
                db.UgcAssignments.Add(assignment); result = assignment.Id;
                Record(actor, "UgcRequestApproved", opportunity.Id, request.CreatorId, Guid.NewGuid(),
                    new { OpportunityId = opportunity.Id, RequestId = request.Id, AssignmentId = assignment.Id, CreatorId = request.CreatorId });
            }
            else
            {
                request.Reject(actor.UserId, reason, Now); result = request.Id;
                Record(actor, "UgcRequestRejected", opportunity.Id, request.CreatorId, Guid.NewGuid(),
                    new { OpportunityId = opportunity.Id, RequestId = request.Id, CreatorId = request.CreatorId, Reason = Clean(reason, 1000) });
            }
            operation.Remember(actor, "ReviewUgcRequest", key, fingerprint, result.ToString(), Now);
            return result;
        }, ct);

    public Task<Guid> SubmitAsync(Actor actor, Guid assignmentId, UgcSubmissionInput input, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var url = ExternalUrl(input.SubmissionUrl);
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), url);
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "SubmitUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            if (assignment.CreatorId != actor.CreatorId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC assignment belongs to another Creator.");
            assignment.Submitted();
            var submission = new UgcSubmission(assignment.Id, assignment.AcceptedRevisionNumber, url, Now);
            db.UgcSubmissions.Add(submission);
            operation.Remember(actor, "SubmitUgc", key, fingerprint, submission.Id.ToString(), Now);
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            Record(actor, "UgcContentSubmitted", opportunity.Id, assignment.CreatorId, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, SubmissionId = submission.Id, opportunity.BusinessId });
            return submission.Id;
        }, ct);

    public Task<Guid> ReviewSubmissionAsync(Actor actor, Guid assignmentId, string action, string? feedback, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var normalized = action.Trim().ToLowerInvariant();
            if (normalized is not ("approve" or "changes" or "reject")) throw new ApplicationFailure(FailureKind.Validation, "Choose Approve, Request Changes, or Reject.");
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), normalized, feedback?.Trim() ?? "");
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "ReviewUgcSubmission", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            var opportunity = await Opportunity(assignment.UgcOpportunityId, token); Own(actor, opportunity);
            var submission = await db.UgcSubmissions.Where(x => x.UgcAssignmentId == assignment.Id && x.Status == UgcAssignmentStatus.Submitted)
                .OrderByDescending(x => x.SubmittedAtUtc).FirstOrDefaultAsync(token)
                ?? throw new ApplicationFailure(FailureKind.Validation, "No submitted UGC content is awaiting review.");
            var correlation = Guid.NewGuid();
            if (normalized == "changes")
            {
                var message = Clean(feedback, 2000) ?? throw new ApplicationFailure(FailureKind.Validation, "Feedback is required when requesting changes.");
                assignment.RequestChanges(); submission.RequestChanges(actor.UserId, message, Now);
                Record(actor, "UgcChangesRequested", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId, Feedback = message });
            }
            else if (normalized == "reject")
            {
                assignment.Reject(Now); submission.Reject(actor.UserId, Clean(feedback, 2000), Now);
                // Rejected accepted work remains reserved until an explicit governed cancellation policy exists.
                Record(actor, "UgcContentRejected", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId, Feedback = Clean(feedback, 2000) });
            }
            else
            {
                var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
                opportunity.RecognizeApprovedDeliverable(wallet, Now, correlation);
                assignment.Approve(Now); submission.Approve(actor.UserId, Now);
                var total = assignment.CreatorPayment.Add(assignment.PlatformFee);
                var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId, JournalSourceType.UgcApproval, Now, key);
                journal.AddLine(JournalLineType.Debit, total, "UgcAllocatedReserve");
                journal.AddLine(JournalLineType.Credit, assignment.CreatorPayment, "CreatorPayable");
                if (assignment.PlatformFee.Amount > 0) journal.AddLine(JournalLineType.Credit, assignment.PlatformFee, "PlatformRevenue");
                journal.Post(); db.FinancialJournals.Add(journal); JournalContext(journal, opportunity, assignment);
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, total, "UgcConsumed", journal.Id, Now, opportunity.Id));
                db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, assignment.Id, total, "Approved", journal.Id, Now));
                var earnings = await db.CreatorEarningsAccounts.SingleOrDefaultAsync(x => x.CreatorId == assignment.CreatorId, token);
                if (earnings is null) { earnings = new CreatorEarningsAccount(assignment.CreatorId); db.CreatorEarningsAccounts.Add(earnings); }
                var before = earnings.AvailableEarnings;
                earnings.EarnUgc(assignment.CreatorPayment, assignment.Id, Now, correlation);
                var earning = earnings.Entries.Last(); db.CreatorEarningEntries.Add(earning); db.Entry(earning).Property("JournalId").CurrentValue = journal.Id;
                if (assignment.PlatformFee.Amount > 0)
                {
                    var revenue = new PlatformRevenueEntry(Guid.NewGuid(), null, PlatformRevenueSource.UgcFee,
                        assignment.PlatformFee, RevenueStatus.Accrued, Now, correlation, assignment.Id);
                    db.PlatformRevenueEntries.Add(revenue); db.Entry(revenue).Property("JournalId").CurrentValue = journal.Id;
                }
                await operation.EmitEligibility(PayoutBeneficiary.Creator, assignment.CreatorId, before, earnings.AvailableEarnings, Now, token);
                Record(actor, "UgcContentApproved", opportunity.Id, assignment.CreatorId, correlation,
                    new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, CreatorId = assignment.CreatorId,
                        CreatorPayment = assignment.CreatorPayment.Amount, PlatformFee = assignment.PlatformFee.Amount, JournalId = journal.Id });
            }
            operation.Remember(actor, "ReviewUgcSubmission", key, fingerprint, assignment.Id.ToString(), Now);
            return assignment.Id;
        }, ct);

    public Task<Guid> UpdateAsync(Actor actor, Guid id, UgcRevisionInput input, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), JsonSerializer.Serialize(input), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "UpdateUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token); Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            var resources = Resources(input.Resources);
            var isDraft = opportunity.Status == UgcOpportunityStatus.Draft;
            var isMaterial = !isDraft && (input.IsMaterial
                || !string.Equals(opportunity.Instructions.Trim(), input.Instructions.Trim(), StringComparison.Ordinal)
                || !string.Equals(opportunity.Location?.Trim(), input.Location?.Trim(), StringComparison.Ordinal)
                || !string.Equals(opportunity.UsageRights?.Trim(), input.UsageRights?.Trim(), StringComparison.Ordinal));
            if (isDraft)
            {
                var contentType = opportunity.ContentType;
                if (input.ContentType is { } requested && (!Enum.TryParse<UgcContentType>(requested, true, out contentType) || !Enum.IsDefined(contentType)))
                    throw new ApplicationFailure(FailureKind.Validation, "Choose Video or Photos.");
                var requirements = input.PlatformRequirements is null
                    ? opportunity.PlatformRequirements.Select(x => (x.Platform, x.Format, x.MinimumAudience)).ToArray()
                    : Requirements(input.PlatformRequirements);
                // Platform requirements are an owned draft detail but deliberately
                // use a restrictive FK. Remove the prior rows explicitly inside the
                // serializable transaction and detach the old instances before the
                // aggregate swaps the collection, avoiding required-FK orphans while
                // retaining restrictive deletion for the parent opportunity.
                await db.UgcPlatformRequirements.Where(x => x.UgcOpportunityId == opportunity.Id).ExecuteDeleteAsync(token);
                db.ChangeTracker.Clear();
                opportunity = await Opportunity(id, token); Own(actor, opportunity);
                if (opportunity.Version != expectedVersion) throw Conflict();
                opportunity.UpdateDraft(input.Title ?? opportunity.Title, input.Slogan, contentType, input.Instructions,
                    JsonSerializer.Serialize(resources), input.Location, input.DueDateUtc ?? opportunity.DueDateUtc,
                    input.ProductProvided ?? opportunity.ProductProvided, input.CreatorMustPurchase ?? opportunity.CreatorMustPurchase,
                    input.UsageRights, input.CreatorPayment is { } payment ? Amount(payment) : opportunity.CreatorPayment,
                    input.CreatorsNeeded ?? opportunity.CreatorCapacity, requirements, Now);
                db.UgcPlatformRequirements.AddRange(opportunity.PlatformRequirements);
            }
            else
            {
                if (input.Title is not null || input.ContentType is not null || input.DueDateUtc is not null
                    || input.ProductProvided is not null || input.CreatorMustPurchase is not null
                    || input.CreatorPayment is not null || input.CreatorsNeeded is not null || input.PlatformRequirements is not null)
                    throw new ApplicationFailure(FailureKind.Validation, "Published UGC financial and deliverable terms cannot be rewritten. Create a new UGC opportunity for those changes.");
                opportunity.UpdateNonMaterial(input.Slogan, input.Instructions, JsonSerializer.Serialize(resources), input.Location, input.UsageRights);
            }
            opportunity.StartRevision();
            if (isMaterial && opportunity.Status is UgcOpportunityStatus.Open or UgcOpportunityStatus.InProgress)
                foreach (var assignment in await db.UgcAssignments.Where(x => x.UgcOpportunityId == id).ToListAsync(token)) assignment.RequireRevisionAcceptance();
            db.UgcRevisions.Add(new(opportunity.Id, opportunity.CurrentRevision, isMaterial, Snapshot(opportunity), actor.UserId, Now));
            operation.Remember(actor, "UpdateUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, isMaterial ? "UgcMaterialRevision" : "UgcUpdated", opportunity.Id, null, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, opportunity.CurrentRevision, IsMaterial = isMaterial });
            return opportunity.Id;
        }, ct);

    public Task<Guid> AcceptRevisionAsync(Actor actor, Guid assignmentId, int revision, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandCreator(actor, token);
            var fingerprint = RequestFingerprint.Create(assignmentId.ToString(), revision.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "AcceptUgcRevision", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var assignment = await db.UgcAssignments.SingleOrDefaultAsync(x => x.Id == assignmentId, token)
                ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC assignment not found.");
            if (assignment.CreatorId != actor.CreatorId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC assignment belongs to another Creator.");
            var opportunity = await db.UgcOpportunities.SingleAsync(x => x.Id == assignment.UgcOpportunityId, token);
            if (revision != opportunity.CurrentRevision) throw new ApplicationFailure(FailureKind.Validation, "Review the latest UGC requirements before accepting.");
            assignment.AcceptRevision(revision);
            operation.Remember(actor, "AcceptUgcRevision", key, fingerprint, assignment.Id.ToString(), Now);
            Record(actor, "UgcRevisionAccepted", opportunity.Id, assignment.CreatorId, Guid.NewGuid(),
                new { OpportunityId = opportunity.Id, AssignmentId = assignment.Id, Revision = revision });
            return assignment.Id;
        }, ct);

    public Task<Guid> CancelAsync(Actor actor, Guid id, long expectedVersion, string key, CancellationToken ct) =>
        Transaction.ExecuteAsync(async token =>
        {
            await DemandBusiness(actor, token);
            var fingerprint = RequestFingerprint.Create(id.ToString(), expectedVersion.ToString());
            var operation = new FinancialOperation(db);
            if (await operation.Replay(actor, "CancelUgc", key, fingerprint, token) is { } replay) return Guid.Parse(replay);
            var opportunity = await Opportunity(id, token); Own(actor, opportunity);
            if (opportunity.Version != expectedVersion) throw Conflict();
            var released = opportunity.ReservedFunding;
            var wallet = await db.BusinessWallets.SingleAsync(x => x.BusinessId == opportunity.BusinessId, token);
            var correlation = Guid.NewGuid(); opportunity.Cancel(wallet, Now, correlation);
            if (released.Amount > 0)
            {
                var journal = Journal(actor, JournalSourceType.UgcReservation, released, "UgcAllocatedReserve", "BusinessAvailable",
                    key, correlation, opportunity.Id, null);
                db.WalletEntries.Add(new(Guid.NewGuid(), opportunity.BusinessId, null, released, "UgcReleased", journal.Id, Now, opportunity.Id));
                db.UgcBudgetEntries.Add(new(Guid.NewGuid(), opportunity.Id, null, released, "Released", journal.Id, Now));
            }
            operation.Remember(actor, "CancelUgc", key, fingerprint, opportunity.Id.ToString(), Now);
            Record(actor, "UgcCancelled", opportunity.Id, null, correlation, new { OpportunityId = opportunity.Id, Released = released.Amount });
            return opportunity.Id;
        }, ct);

    public async Task<IReadOnlyList<UgcCard>> BusinessAsync(Actor actor, CancellationToken ct)
    {
        await DemandBusiness(actor, ct);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements)
            .Where(x => x.BusinessId == actor.BusinessId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var name = (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId, ct)).DisplayName;
        return rows.Select(x => Card(x, name, null)).ToArray();
    }

    public async Task<IReadOnlyList<UgcCard>> DiscoverAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var socials = await db.CreatorSocialProfiles.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId && x.IsActive).ToListAsync(ct);
        var requests = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId)
            .OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements)
            .Where(x => x.Status == UgcOpportunityStatus.Open && x.DueDateUtc > Now).OrderByDescending(x => x.PublishedAtUtc).ToListAsync(ct);
        var businessIds = rows.Select(x => x.BusinessId).Distinct().ToArray();
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && businessIds.Contains(x.SubjectId))
            .ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        return rows.Where(x => Eligible(socials, x) && x.ApprovedCreatorCount < x.CreatorCapacity)
            .Select(x => Card(x, names.GetValueOrDefault(x.BusinessId, "Business"), requests.FirstOrDefault(r => r.UgcOpportunityId == x.Id)?.Status.ToString())).ToArray();
    }

    public async Task<IReadOnlyList<UgcRequestView>> CreatorRequestsAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var requests = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        var name = (await db.PublicWorkspaceProfiles.AsNoTracking().SingleAsync(x => x.Role == ActorRole.Creator && x.SubjectId == actor.CreatorId, ct)).DisplayName;
        return requests.Select(x => new UgcRequestView(x.Id, x.UgcOpportunityId, x.CreatorId, name, x.Status.ToString(), x.RequestedAtUtc, x.RejectionReason)).ToArray();
    }

    public async Task<IReadOnlyList<UgcAssignmentView>> CreatorAssignmentsAsync(Actor actor, CancellationToken ct)
    {
        await DemandCreator(actor, ct);
        var rows = await db.UgcAssignments.AsNoTracking().Where(x => x.CreatorId == actor.CreatorId).OrderByDescending(x => x.ApprovedAtUtc).ToListAsync(ct);
        return await AssignmentViews(rows, ct);
    }

    public async Task<UgcDetail> DetailAsync(Actor actor, Guid id, CancellationToken ct)
    {
        var opportunity = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC opportunity not found.");
        var isBusiness = actor.Role == ActorRole.Business && actor.BusinessId == opportunity.BusinessId;
        var isCreator = actor.Role == ActorRole.Creator && actor.CreatorId is not null;
        var isAdmin = actor.Role is ActorRole.PlatformAdmin or ActorRole.OperationsAdmin;
        if (!isBusiness && !isCreator && !isAdmin) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity is not available to you.");
        if (isBusiness) await DemandBusiness(actor, ct);
        else if (isCreator)
        {
            await DemandCreator(actor, ct);
            var creatorHasContext = opportunity.Status == UgcOpportunityStatus.Open && opportunity.DueDateUtc > Now
                || await db.UgcCreatorRequests.AsNoTracking().AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId, ct)
                || await db.UgcAssignments.AsNoTracking().AnyAsync(x => x.UgcOpportunityId == id && x.CreatorId == actor.CreatorId, ct);
            if (!creatorHasContext) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity is not available to the active Creator.");
        }
        else DemandAdmin(actor);
        var business = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && x.SubjectId == opportunity.BusinessId).Select(x => x.DisplayName).SingleAsync(ct);
        var requestRows = await db.UgcCreatorRequests.AsNoTracking().Where(x => x.UgcOpportunityId == id).OrderByDescending(x => x.RequestedAtUtc).ToListAsync(ct);
        if (isCreator) requestRows = requestRows.Where(x => x.CreatorId == actor.CreatorId).ToList();
        var creatorIds = requestRows.Select(x => x.CreatorId).Distinct().ToArray();
        var creatorNames = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Creator && creatorIds.Contains(x.SubjectId)).ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        var requests = requestRows.Select(x => new UgcRequestView(x.Id, id, x.CreatorId, creatorNames.GetValueOrDefault(x.CreatorId, "Creator"), x.Status.ToString(), x.RequestedAtUtc, x.RejectionReason)).ToArray();
        var assignmentRows = await db.UgcAssignments.AsNoTracking().Where(x => x.UgcOpportunityId == id && (!isCreator || x.CreatorId == actor.CreatorId)).ToListAsync(ct);
        var assignments = await AssignmentViews(assignmentRows, ct);
        var revisions = await db.UgcRevisions.AsNoTracking().Where(x => x.UgcOpportunityId == id)
            .OrderByDescending(x => x.RevisionNumber)
            .Select(x => new UgcRevisionView(x.RevisionNumber, x.IsMaterial, x.CreatedAtUtc, x.SnapshotJson)).ToListAsync(ct);
        var status = isCreator ? requestRows.OrderByDescending(x => x.RequestedAtUtc).FirstOrDefault()?.Status.ToString() : null;
        return new UgcDetail(Card(opportunity, business, status), opportunity.Instructions, ParseResources(opportunity.ResourcesJson),
            opportunity.ProductProvided, opportunity.CreatorMustPurchase, opportunity.UsageRights, opportunity.CurrentRevision, requests, assignments, revisions);
    }

    public async Task<IReadOnlyList<UgcCard>> AdminAsync(Actor actor, CancellationToken ct)
    {
        DemandAdmin(actor);
        var rows = await db.UgcOpportunities.AsNoTracking().Include(x => x.PlatformRequirements).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct);
        var ids = rows.Select(x => x.BusinessId).Distinct().ToArray();
        var names = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => x.Role == ActorRole.Business && ids.Contains(x.SubjectId)).ToDictionaryAsync(x => x.SubjectId, x => x.DisplayName, ct);
        return rows.Select(x => Card(x, names.GetValueOrDefault(x.BusinessId, "Business"), null)).ToArray();
    }

    private async Task<IReadOnlyList<UgcAssignmentView>> AssignmentViews(IReadOnlyCollection<UgcAssignment> rows, CancellationToken ct)
    {
        var opportunityIds = rows.Select(x => x.UgcOpportunityId).Distinct().ToArray();
        var opportunities = await db.UgcOpportunities.AsNoTracking().Where(x => opportunityIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var businessIds = opportunities.Values.Select(x => x.BusinessId).Distinct().ToArray();
        var creatorIds = rows.Select(x => x.CreatorId).Distinct().ToArray();
        var profiles = await db.PublicWorkspaceProfiles.AsNoTracking().Where(x => businessIds.Contains(x.SubjectId) || creatorIds.Contains(x.SubjectId)).ToListAsync(ct);
        var submissions = await db.UgcSubmissions.AsNoTracking().Where(x => rows.Select(r => r.Id).Contains(x.UgcAssignmentId)).OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct);
        return rows.Select(x =>
        {
            var opportunity = opportunities[x.UgcOpportunityId]; var submission = submissions.FirstOrDefault(s => s.UgcAssignmentId == x.Id);
            return new UgcAssignmentView(x.Id, x.UgcOpportunityId, opportunity.Title, opportunity.BusinessId,
                profiles.FirstOrDefault(p => p.SubjectId == opportunity.BusinessId && p.Role == ActorRole.Business)?.DisplayName ?? "Business",
                x.CreatorId, profiles.FirstOrDefault(p => p.SubjectId == x.CreatorId && p.Role == ActorRole.Creator)?.DisplayName ?? "Creator",
                x.CreatorPayment.Amount, x.Status.ToString(), x.AcceptedRevisionNumber, x.RevisionAcceptanceRequired,
                opportunity.DueDateUtc, opportunity.Instructions, ParseResources(opportunity.ResourcesJson), opportunity.Location,
                submission?.Feedback, submission?.SubmissionUrl);
        }).ToArray();
    }

    private async Task<UgcOpportunity> Opportunity(Guid id, CancellationToken ct) =>
        await db.UgcOpportunities.Include(x => x.PlatformRequirements).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new ApplicationFailure(FailureKind.NotFound, "UGC opportunity not found.");
    private async Task DemandBusiness(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Business || actor.BusinessId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Business && x.SubjectId == actor.BusinessId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "This Business workspace is not available to you.");
    }
    private async Task DemandCreator(Actor actor, CancellationToken ct)
    {
        if (actor.Role != ActorRole.Creator || actor.CreatorId is null ||
            !await db.CommercePermissions.AnyAsync(x => x.UserId == actor.UserId && x.Role == ActorRole.Creator && x.SubjectId == actor.CreatorId && x.IsActive, ct))
            throw new ApplicationFailure(FailureKind.Forbidden, "This Creator workspace is not available to you.");
    }
    private static void DemandAdmin(Actor actor)
    { if (actor.Role is not (ActorRole.PlatformAdmin or ActorRole.OperationsAdmin)) throw new ApplicationFailure(FailureKind.Forbidden, "Admin access is required."); }
    private static void Own(Actor actor, UgcOpportunity opportunity)
    { if (actor.BusinessId != opportunity.BusinessId) throw new ApplicationFailure(FailureKind.Forbidden, "This UGC opportunity belongs to another Business."); }

    private async Task EnsureCreatorEligibility(Guid creatorId, UgcOpportunity opportunity, CancellationToken ct)
    {
        var socials = await db.CreatorSocialProfiles.AsNoTracking().Where(x => x.CreatorId == creatorId && x.IsActive).ToListAsync(ct);
        if (!Eligible(socials, opportunity)) throw new ApplicationFailure(FailureKind.Validation, "Your Creator social profiles do not meet this UGC opportunity's optional platform requirement.");
    }
    private static bool Eligible(IReadOnlyCollection<CreatorSocialProfileRecord> socials, UgcOpportunity opportunity) =>
        opportunity.PlatformRequirements.Where(x => x.MinimumAudience is not null).All(requirement =>
            socials.Any(s => s.Platform == requirement.Platform && s.SelfReportedAudience >= requirement.MinimumAudience));

    private FinancialJournal Journal(Actor actor, JournalSourceType source, Money amount, string debit, string credit,
        string key, Guid correlation, Guid opportunityId, Guid? assignmentId)
    {
        var journal = new FinancialJournal(Guid.NewGuid().ToString("N"), correlation, actor.UserId, source, Now, key);
        journal.AddLine(JournalLineType.Debit, amount, debit); journal.AddLine(JournalLineType.Credit, amount, credit); journal.Post();
        db.FinancialJournals.Add(journal);
        db.Entry(journal).Property("BusinessId").CurrentValue = actor.BusinessId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunityId;
        db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignmentId;
        return journal;
    }
    private void JournalContext(FinancialJournal journal, UgcOpportunity opportunity, UgcAssignment assignment)
    {
        db.Entry(journal).Property("BusinessId").CurrentValue = opportunity.BusinessId;
        db.Entry(journal).Property("CreatorId").CurrentValue = assignment.CreatorId;
        db.Entry(journal).Property("UgcOpportunityId").CurrentValue = opportunity.Id;
        db.Entry(journal).Property("UgcAssignmentId").CurrentValue = assignment.Id;
    }
    private void Record(Actor actor, string type, Guid opportunityId, Guid? creatorId, Guid correlation, object payload)
    {
        db.AuditEvents.Add(new(Guid.NewGuid(), type, actor.UserId, actor.BusinessId, null, creatorId, correlation, Now, "", opportunityId));
        db.OutboxMessages.Add(new() { EventType = type, Payload = JsonSerializer.Serialize(payload), OccurredAtUtc = Now });
    }
    private static Money Amount(decimal value)
    { if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value) throw new ApplicationFailure(FailureKind.Validation, "Enter a positive amount with no more than two decimal places."); return new(value); }
    private static string ExternalUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new ApplicationFailure(FailureKind.Validation, "Enter a secure HTTPS submission link.");
        return uri.AbsoluteUri;
    }
    private static string[] Resources(IReadOnlyList<string>? resources) => (resources ?? [])
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(ExternalUrl).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    private static IReadOnlyList<(CreatorPlatform Platform, string Format, long? MinimumAudience)> Requirements(IReadOnlyList<UgcPlatformRequirementInput>? input)
    {
        var result = new List<(CreatorPlatform, string, long?)>();
        foreach (var row in input ?? [])
        {
            if (!Enum.TryParse<CreatorPlatform>(row.Platform, true, out var platform) || !Enum.IsDefined(platform))
                throw new ApplicationFailure(FailureKind.Validation, "Choose a supported social platform.");
            result.Add((platform, row.Format, row.MinimumAudience));
        }
        return result;
    }
    private static UgcCard Card(UgcOpportunity x, string business, string? requestStatus) => new(x.Id, x.BusinessId, business,
        x.Title, x.Slogan, x.ContentType.ToString(), x.Status.ToString(), x.CreatorPayment.Amount, x.CreatorCapacity,
        x.ApprovedCreatorCount, x.RequiredFunding.Amount, x.ReservedFunding.Amount, x.UsedFunding.Amount, x.DueDateUtc,
        x.Location, x.PlatformRequirements.Select(p => new UgcPlatformRequirementView(p.Platform.ToString(), p.Format, p.MinimumAudience)).ToArray(), requestStatus, x.Version);
    private static string[] ParseResources(string json)
    { try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return []; } }
    private static string Snapshot(UgcOpportunity x) => JsonSerializer.Serialize(new
    { x.Title, x.Slogan, x.ContentType, x.Instructions, x.ResourcesJson, x.Location, x.DueDateUtc, x.ProductProvided, x.CreatorMustPurchase, x.UsageRights, x.CreatorPayment, x.CreatorCapacity, x.CurrentRevision });
    private static string? Clean(string? value, int max)
    { if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim(); if (result.Length > max) throw new ApplicationFailure(FailureKind.Validation, "The supplied information is too long."); return result; }
    private static ApplicationFailure Conflict() => new(FailureKind.ConcurrencyConflict, "UGC changed. Reload before trying again.");
}
