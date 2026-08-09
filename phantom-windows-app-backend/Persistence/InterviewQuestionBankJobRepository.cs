using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Phantom.WindowsApp.Backend.Persistence;

public sealed class InterviewQuestionBankJobRepository
{
    private readonly PostgresBackendStore _store;

    public InterviewQuestionBankJobRepository(PostgresBackendStore store)
    {
        _store = store;
    }

    public void Enqueue(
        string sessionId,
        string userId,
        IReadOnlyList<string> questionInputs,
        DateTime interviewStartedAtUtc,
        DateTime interviewEndedAtUtc,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO interview_question_bank_jobs (
    job_id, session_id, user_id, raw_question_inputs_json, status,
    interview_started_at_utc, interview_ended_at_utc,
    requested_at_utc, updated_at_utc
) VALUES (
    @jobId, @sessionId, @userId, @questionInputs, 'queued',
    @interviewStartedAtUtc, @interviewEndedAtUtc,
    NOW(), NOW()
)
ON CONFLICT (session_id) DO NOTHING;";
        command.Parameters.AddWithValue("jobId", $"question-bank-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("sessionId", sessionId);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("questionInputs", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(questionInputs));
        command.Parameters.AddWithValue("interviewStartedAtUtc", interviewStartedAtUtc);
        command.Parameters.AddWithValue("interviewEndedAtUtc", interviewEndedAtUtc);
        command.ExecuteNonQuery();
    }

    public InterviewQuestionBankJob? TryStartNextQueued()
    {
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
WITH next_job AS (
    SELECT job_id
    FROM interview_question_bank_jobs
    WHERE status = 'queued'
    ORDER BY requested_at_utc ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE interview_question_bank_jobs jobs
SET status = 'running', started_at_utc = NOW(), updated_at_utc = NOW()
FROM next_job
WHERE jobs.job_id = next_job.job_id
RETURNING jobs.*;";
        using var reader = command.ExecuteReader();
        var job = reader.Read() ? Map(reader) : null;
        reader.Close();
        transaction.Commit();
        return job;
    }

    public void RequeueRunningJobs()
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE interview_question_bank_jobs
SET status = 'queued', started_at_utc = NULL, updated_at_utc = NOW()
WHERE status = 'running';";
        command.ExecuteNonQuery();
    }

    public void MarkCompleted(InterviewQuestionBankJob job, IReadOnlyList<string> questions)
    {
        var questionsJson = JsonSerializer.Serialize(questions);
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE interview_question_bank_jobs
SET status = 'completed', raw_question_inputs_json = '[]'::jsonb, questions_json = @questions, error = '',
    completed_at_utc = NOW(), updated_at_utc = NOW()
WHERE job_id = @jobId;";
            command.Parameters.AddWithValue("jobId", job.JobId);
            command.Parameters.AddWithValue("questions", NpgsqlDbType.Jsonb, questionsJson);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO dashboard_interview_question_banks (
    session_id, user_id, questions_json,
    interview_started_at_utc, interview_ended_at_utc, created_at_utc
) VALUES (
    @sessionId, @userId, @questions,
    @interviewStartedAtUtc, @interviewEndedAtUtc, NOW()
)
ON CONFLICT (session_id) DO UPDATE SET
    questions_json = EXCLUDED.questions_json,
    interview_started_at_utc = EXCLUDED.interview_started_at_utc,
    interview_ended_at_utc = EXCLUDED.interview_ended_at_utc;";
            command.Parameters.AddWithValue("sessionId", job.SessionId);
            command.Parameters.AddWithValue("userId", job.UserId);
            command.Parameters.AddWithValue("questions", NpgsqlDbType.Jsonb, questionsJson);
            command.Parameters.AddWithValue("interviewStartedAtUtc", job.InterviewStartedAtUtc);
            command.Parameters.AddWithValue("interviewEndedAtUtc", job.InterviewEndedAtUtc);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void MarkFailed(string jobId, string error)
    {
        using var connection = _store.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE interview_question_bank_jobs
SET status = 'failed', error = @error, completed_at_utc = NOW(), updated_at_utc = NOW()
WHERE job_id = @jobId;";
        command.Parameters.AddWithValue("jobId", jobId);
        command.Parameters.AddWithValue("error", error);
        command.ExecuteNonQuery();
    }

    private static InterviewQuestionBankJob Map(NpgsqlDataReader reader)
    {
        return new InterviewQuestionBankJob(
            reader.GetString(reader.GetOrdinal("job_id")),
            reader.GetString(reader.GetOrdinal("session_id")),
            reader.GetString(reader.GetOrdinal("user_id")),
            reader.GetString(reader.GetOrdinal("raw_question_inputs_json")),
            reader.GetDateTime(reader.GetOrdinal("interview_started_at_utc")),
            reader.GetDateTime(reader.GetOrdinal("interview_ended_at_utc")));
    }
}

public sealed record InterviewQuestionBankJob(
    string JobId,
    string SessionId,
    string UserId,
    string RawQuestionInputsJson,
    DateTime InterviewStartedAtUtc,
    DateTime InterviewEndedAtUtc);
