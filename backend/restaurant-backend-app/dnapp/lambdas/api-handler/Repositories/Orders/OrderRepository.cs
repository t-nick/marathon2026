using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants;
using APIHandler.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Orders;

public class OrderRepository : IOrderRepository
{
    private readonly string OrdersTableName = ResourceNames.OrdersTableName;
    private readonly string CustomerIdGlobalIndex = ResourceNames.OrdersTableCustomerIdGlobalIndex;
    private readonly string ReservationIdGlobalIndex = ResourceNames.OrdersTableReservationIdGlobalIndex;

    private readonly IDynamoDBContext _dynamoDbContext;
    private readonly IAmazonDynamoDB _dynamoDbClient;

    public OrderRepository(IDynamoDBContext dynamoDbContext, IAmazonDynamoDB dynamoDbClient)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
        _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
    }

    public async Task<Order?> GetOrderByIdAsync(string orderId, string customerId)
    {
        return await _dynamoDbContext.LoadAsync<Order>(orderId, customerId, DynamoConfig.LoadConfig(OrdersTableName));
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = CustomerIdGlobalIndex,
        };

        var query = _dynamoDbContext.QueryAsync<Order>(customerId, config);

        return await query.GetRemainingAsync();
    }

    public async Task<Order?> GetOrderByReservationIdAsync(string reservationId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = ReservationIdGlobalIndex,
        };

        var results = await _dynamoDbContext.QueryAsync<Order>(reservationId, config).GetNextSetAsync();

        return results.FirstOrDefault(o => o.Status != OrderStatus.Cancelled);
    }

    public async Task SaveOrderAsync(Order order)
    {
        await _dynamoDbContext.SaveAsync(order, DynamoConfig.SaveConfig(OrdersTableName));
    }

    public async Task SubmitGroupReservationAtomicallyAsync(
        Order order,
        GroupReservation groupReservation,
        OrderStatus expectedOrderStatus,
        GroupReservationStatus expectedGroupReservationStatus,
        long? expectedSubmissionRevision)
    {
        string orderVersionCondition = order.Version.HasValue
            ? "#version = :expectedVersion"
            : "attribute_not_exists(#version)";

        var orderExpressionValues = new Dictionary<string, AttributeValue>
        {
            [":dishes"] = ToDynamoDbDishes(order.Dishes),
            [":newStatus"] = ToDynamoDbNumber(order.Status),
            [":expectedStatus"] = ToDynamoDbNumber(expectedOrderStatus),
            [":newVersion"] = ToDynamoDbNumber((order.Version ?? 0) + 1)
        };
        if (order.Version.HasValue)
            orderExpressionValues[":expectedVersion"] = ToDynamoDbNumber(order.Version.Value);

        string groupRevisionCondition = expectedSubmissionRevision.HasValue
            ? "#submissionRevision = :expectedRevision"
            : "attribute_not_exists(#submissionRevision)";

        var groupExpressionValues = new Dictionary<string, AttributeValue>
        {
            [":expectedStatusString"] = ToDynamoDbString(expectedGroupReservationStatus.ToString()),
            [":expectedStatusNumber"] = ToDynamoDbNumber((int)expectedGroupReservationStatus),
            [":submittedStatusString"] = ToDynamoDbString(GroupReservationStatus.Submitted.ToString()),
            [":submittedStatusNumber"] = ToDynamoDbNumber((int)GroupReservationStatus.Submitted)
        };
        if (expectedSubmissionRevision.HasValue)
            groupExpressionValues[":expectedRevision"] = ToDynamoDbLong(expectedSubmissionRevision.Value);

        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = OrdersTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["orderId"] = new() { S = order.OrderId },
                        ["customerId"] = new() { S = order.CustomerId }
                    },
                    UpdateExpression = "SET #dishes = :dishes, #status = :newStatus, #version = :newVersion",
                    ConditionExpression = $"#status = :expectedStatus AND {orderVersionCondition}",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#dishes"] = "dishes",
                        ["#status"] = "status",
                        ["#version"] = "version"
                    },
                    ExpressionAttributeValues = orderExpressionValues
                }
            },
            new()
            {
                Update = new Update
                {
                    TableName = ResourceNames.GroupReservationsTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["groupReservationId"] = new() { S = groupReservation.GroupReservationId }
                    },
                    UpdateExpression = "SET #status = :submittedStatus",
                    ConditionExpression = $"(#status = :expectedStatusString OR #status = :expectedStatusNumber) " +
                        $"AND #status <> :submittedStatusString AND #status <> :submittedStatusNumber AND {groupRevisionCondition}",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "status",
                        ["#submissionRevision"] = "submissionRevision"
                    },
                    ExpressionAttributeValues = groupExpressionValues
                }
            }
        };

        await _dynamoDbClient.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = transactItems
        });
    }

    private static AttributeValue ToDynamoDbDishes(IEnumerable<OrderItem> dishes) => new()
    {
        L = dishes.Select(dish => new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["dishId"] = ToDynamoDbString(dish.DishId),
                ["quantity"] = ToDynamoDbNumber(dish.Quantity),
                ["state"] = ToDynamoDbNumber(dish.State)
            }
        }).ToList()
    };

    private static AttributeValue ToDynamoDbString(string value) => new() { S = value };

    private static AttributeValue ToDynamoDbNumber<T>(T value) where T : struct, Enum =>
        new() { N = Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) };

    private static AttributeValue ToDynamoDbNumber(int value) =>
        new() { N = value.ToString(CultureInfo.InvariantCulture) };

    private static AttributeValue ToDynamoDbLong(long value) =>
        new() { N = value.ToString(CultureInfo.InvariantCulture) };
}