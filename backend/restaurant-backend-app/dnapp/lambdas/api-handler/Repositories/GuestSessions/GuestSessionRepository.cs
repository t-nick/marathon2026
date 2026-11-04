using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.GuestSessions;

public class GuestSessionRepository : IGuestSessionRepository
{
    private readonly IDynamoDBContext _context;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public GuestSessionRepository(IDynamoDBContext context, IAmazonDynamoDB dynamoDbClient)
    {
        _context = context;
        _dynamoDbClient = dynamoDbClient;
    }

    public async Task<GuestSession?> GetByGuestSessionId(string id) => await _context.LoadAsync<GuestSession?>(id, new LoadConfig()
    {
        OverrideTableName = ResourceNames.GuestSessionTableName,
        ConsistentRead = true
    });

    public async Task<GuestSession?> GetByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await _context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<GuestSession?> GetByTokenHash(string tokenHash)
    {
        List<GuestSession> result = await _context.QueryAsync<GuestSession>(tokenHash, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.SessionTokenHash
        }).GetRemainingAsync();

        return result.FirstOrDefault();
    }

    public async Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string groupReservationId)
    {
        return await _context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();
    }

    public async Task SaveAsync(GuestSession guestSession)
    {
        if (!guestSession.IsSubmitted)
        {
            await _context.SaveAsync(guestSession, new SaveConfig
            {
                OverrideTableName = ResourceNames.GuestSessionTableName
            });
            return;
        }

        await _dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = ResourceNames.GuestSessionTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["guestSessionId"] = new() { S = guestSession.Id }
                        },
                        UpdateExpression = "SET #isSubmitted = :submitted",
                        ConditionExpression = "attribute_exists(#guestSessionId)",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#guestSessionId"] = "guestSessionId",
                            ["#isSubmitted"] = "isSubmitted"
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":submitted"] = new() { BOOL = true }
                        }
                    }
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = ResourceNames.GroupReservationsTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["groupReservationId"] = new() { S = guestSession.GroupReservationId }
                        },
                        UpdateExpression = "SET #submissionRevision = if_not_exists(#submissionRevision, :zero) + :one",
                        ConditionExpression = "#status = :openStatusString OR #status = :openStatusNumber",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#status"] = "status",
                            ["#submissionRevision"] = "submissionRevision"
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":openStatusString"] = new() { S = GroupReservationStatus.Open.ToString() },
                            [":openStatusNumber"] = new() { N = ((int)GroupReservationStatus.Open).ToString() },
                            [":zero"] = new() { N = "0" },
                            [":one"] = new() { N = "1" }
                        }
                    }
                }
            ]
        });
    }

    public async Task<int> CountByGroupReservationId(string groupReservationId)
    {
        List<GuestSession> result = await _context.QueryAsync<GuestSession>(groupReservationId, new QueryConfig
        {
            OverrideTableName = ResourceNames.GuestSessionTableName,
            IndexName = GlobalSecondaryIndexes.GuestSession.GroupReservationId
        }).GetRemainingAsync();

        return result.Count(session => !session.IsOwnerSession);
    }

    public async Task DeleteAsync(GuestSession session)
    {
        await _context.DeleteAsync(session, DynamoConfig.DeleteConfig(ResourceNames.GuestSessionTableName));
    }
}
