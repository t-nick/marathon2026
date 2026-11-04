using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupReservationServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object,
            _orderRepositoryMock.Object);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_ValidInput_ReturnsGroupReservationInfo(
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location,
        string expectedUserName)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(0);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);

        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync(expectedUserName);

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        Assert.Equal(table.LocationId, result.LocationId);
        Assert.Equal(reservation.GuestCount, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
    }

    [Fact]
    public async Task CreateGroupReservationAsync_CustomerReservation_CreatesOwnerSession()
    {
        const string reservationId = "reservation-1";
        const string ownerUserId = "owner-user";
        var reservation = new Reservation
        {
            Id = reservationId,
            TableId = "table-1",
            CustomerId = ownerUserId,
            WaiterId = "waiter-1"
        };
        GuestSession? savedSession = null;
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservationId))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(It.IsAny<string>()))
            .ReturnsAsync([]);
        _guestSessionRepositoryMock
            .Setup(repository => repository.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(session => savedSession = session);

        GroupReservation group = await _service.CreateGroupReservationAsync(reservationId);

        Assert.NotNull(savedSession);
        Assert.Equal(group.GroupReservationId, savedSession.GroupReservationId);
        Assert.Equal(ownerUserId, savedSession.OwnerUserId);
        Assert.True(savedSession.IsOwnerSession);
        Assert.Empty(savedSession.Dishes);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_InvalidToken_ThrowsNotFound(string token)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(token))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationByTokenAsync(token));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsNotOpen_ReturnsReservationInfo(
        GroupReservationStatus status,
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location)
    {
        groupReservation.Status = status;
        groupReservation.ReservationId = reservation.Id;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);
        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);
        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync("Owner");

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_WhenGuestCountIsMax_ReturnsReservationInfo(
        GroupReservation groupReservation,
        Reservation reservation,
        Table table,
        Location location)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;
        reservation.GuestCount = 3;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(2);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);
        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);
        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync("Owner");

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        _guestSessionRepositoryMock.Verify(
            r => r.CountByGroupReservationId(groupReservation.GroupReservationId),
            Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_ValidInput_UpdatesGroupReservationStatus(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(groupReservation);

        GroupReservation? savedGroupReservation = null;
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(r => savedGroupReservation = r);

        await _service.CloseGroupReservationAsync(groupReservation.ReservationId);

        Assert.NotNull(savedGroupReservation);
        Assert.Equal(GroupReservationStatus.Closed, savedGroupReservation.Status);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_GroupReservationNotFound_DoesNotThrowException(string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservationId))
            .ReturnsAsync((GroupReservation?)null);

        await _service.CloseGroupReservationAsync(reservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_OneGuest_ThrowsValidationException(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsValidationException(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    public async Task GetGroupReservationInviteTokenAsync_GroupStatusIsNotOpen_ThrowsException(GroupReservationStatus status, Type type, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = status;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(type, () => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_Owner_UsesSubmittedSessionsAggregatesAndUpdatesExistingOrder()
    {
        const string userId = "owner-user";
        const string reservationId = "reservation-1";
        const string groupReservationId = "group-1";
        var groupReservation = new GroupReservation
        {
            GroupReservationId = groupReservationId,
            ReservationId = reservationId,
            ReservationToken = "token-1",
            Status = GroupReservationStatus.Open,
            SubmissionRevision = 11
        };
        var reservation = new Reservation
        {
            Id = reservationId,
            TableId = "table-1",
            CustomerId = userId,
            WaiterId = "waiter-1"
        };
        var sessions = new List<GuestSession>
        {
            new()
            {
                Id = "submitted-1",
                GroupReservationId = groupReservationId,
                GuestName = "Guest 1",
                IsSubmitted = true,
                Dishes =
                [
                    new OrderItem { DishId = "dish-existing", Quantity = 2 },
                    new OrderItem { DishId = "dish-new", Quantity = 1 }
                ]
            },
            new()
            {
                Id = "submitted-2",
                GroupReservationId = groupReservationId,
                GuestName = "Guest 2",
                IsSubmitted = true,
                Dishes = [new OrderItem { DishId = "dish-existing", Quantity = 3 }]
            },
            new()
            {
                Id = "not-submitted",
                GroupReservationId = groupReservationId,
                GuestName = "Guest 3",
                IsSubmitted = false,
                Dishes = [new OrderItem { DishId = "dish-new", Quantity = 100 }]
            }
        };
        var order = new Order
        {
            OrderId = "order-1",
            CustomerId = userId,
            ReservationId = reservationId,
            ReservationAddress = "Main Street",
            ReservationTime = "7:00 PM",
            Dishes =
            [
                new OrderItem { DishId = "dish-existing", Quantity = 4, State = OrderItemState.Confirmed },
                new OrderItem { DishId = "unrelated-dish", Quantity = 7, State = OrderItemState.Pending }
            ],
            Status = OrderStatus.Created,
            Version = 7
        };
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservationId))
            .ReturnsAsync(reservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservationId))
            .ReturnsAsync(sessions);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservationId))
            .ReturnsAsync(order);

        List<SubmittedDishQuantityResponse> result = await _service.SubmitGroupReservationAsync(
            userId, groupReservationId, reservationId);

        Assert.Equal(
        [
            new SubmittedDishQuantityResponse("dish-existing", 5),
            new SubmittedDishQuantityResponse("dish-new", 1)
        ],
        result);
        Assert.Equal(9, order.Dishes.Single(item => item.DishId == "dish-existing").Quantity);
        Assert.Equal(OrderItemState.Confirmed, order.Dishes.Single(item => item.DishId == "dish-existing").State);
        Assert.Equal(1, order.Dishes.Single(item => item.DishId == "dish-new").Quantity);
        Assert.Equal(OrderItemState.Pending, order.Dishes.Single(item => item.DishId == "dish-new").State);
        Assert.Equal(7, order.Dishes.Single(item => item.DishId == "unrelated-dish").Quantity);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, groupReservation.Status);
        Assert.Equal(11, groupReservation.SubmissionRevision);
        _guestSessionRepositoryMock.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(groupReservationId), Times.Once);
        _orderRepositoryMock.Verify(
            repository => repository.SubmitGroupReservationAtomicallyAsync(
                It.Is<Order>(savedOrder =>
                    savedOrder.OrderId == order.OrderId &&
                    savedOrder.Status == OrderStatus.SubmittedByCustomer &&
                    savedOrder.Version == 7),
                It.Is<GroupReservation>(savedGroup =>
                    savedGroup.GroupReservationId == groupReservationId &&
                    savedGroup.Status == GroupReservationStatus.Submitted &&
                    savedGroup.SubmissionRevision == 11),
                OrderStatus.Created,
                GroupReservationStatus.Open,
                11), Times.Once);
        _orderRepositoryMock.Verify(
            repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(
            repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_AlreadySubmitted_ReturnsAggregateWithoutSubmittingAgain()
    {
        SetupValidSubmitData(
        [
            new GuestSession
            {
                Id = "session-1",
                GroupReservationId = "group-1",
                GuestName = "Guest",
                IsSubmitted = true,
                Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2 }]
            }
        ],
        GroupReservationStatus.Submitted);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync("reservation-1"))
            .ReturnsAsync(new Order
            {
                OrderId = "order-1",
                CustomerId = "owner-user",
                ReservationId = "reservation-1",
                ReservationAddress = "Main Street",
                ReservationTime = "7:00 PM",
                Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2 }],
                Status = OrderStatus.SubmittedByCustomer
            });

        List<SubmittedDishQuantityResponse> result = await _service.SubmitGroupReservationAsync(
            "owner-user", "group-1", "reservation-1");

        Assert.Equal([new SubmittedDishQuantityResponse("dish-1", 2)], result);
        _orderRepositoryMock.Verify(
            repository => repository.SubmitGroupReservationAtomicallyAsync(
                It.IsAny<Order>(),
                It.IsAny<GroupReservation>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<GroupReservationStatus>(),
                It.IsAny<long?>()), Times.Never);
        _orderRepositoryMock.Verify(
            repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(
            repository => repository.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_TransactionCanceledAfterConcurrentCommit_ReturnsAggregateAsIdempotentSuccess()
    {
        SetupValidSubmitData(
        [
            new GuestSession
            {
                Id = "session-1",
                GroupReservationId = "group-1",
                GuestName = "Guest",
                IsSubmitted = true,
                Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2 }]
            }
        ],
        setupGroupReservation: false);
        _groupReservationRepositoryMock
            .SetupSequence(repository => repository.GetGroupReservationByIdAsync("group-1"))
            .ReturnsAsync(CreateGroupReservation(GroupReservationStatus.Open))
            .ReturnsAsync(CreateGroupReservation(GroupReservationStatus.Submitted));
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync("reservation-1"))
            .ReturnsAsync(CreateSubmitOrder());
        _orderRepositoryMock
            .Setup(repository => repository.SubmitGroupReservationAtomicallyAsync(
                It.IsAny<Order>(),
                It.IsAny<GroupReservation>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<GroupReservationStatus>(),
                It.IsAny<long?>()))
            .ThrowsAsync(new Amazon.DynamoDBv2.Model.TransactionCanceledException("Conditional check failed."));

        List<SubmittedDishQuantityResponse> result = await _service.SubmitGroupReservationAsync(
            "owner-user", "group-1", "reservation-1");

        Assert.Equal([new SubmittedDishQuantityResponse("dish-1", 2)], result);
        _orderRepositoryMock.Verify(
            repository => repository.SubmitGroupReservationAtomicallyAsync(
                It.IsAny<Order>(),
                It.IsAny<GroupReservation>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<GroupReservationStatus>(),
                It.IsAny<long?>()), Times.Once);
        _groupReservationRepositoryMock.Verify(
            repository => repository.GetGroupReservationByIdAsync("group-1"), Times.Exactly(2));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_TransactionCanceledWithoutConcurrentCommit_RethrowsFailure()
    {
        SetupValidSubmitData(
        [
            new GuestSession
            {
                Id = "session-1",
                GroupReservationId = "group-1",
                GuestName = "Guest",
                IsSubmitted = true,
                Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2 }]
            }
        ],
        setupGroupReservation: false);
        _groupReservationRepositoryMock
            .SetupSequence(repository => repository.GetGroupReservationByIdAsync("group-1"))
            .ReturnsAsync(CreateGroupReservation(GroupReservationStatus.Open))
            .ReturnsAsync(CreateGroupReservation(GroupReservationStatus.Open));
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync("reservation-1"))
            .ReturnsAsync(CreateSubmitOrder());
        var transactionFailure = new Amazon.DynamoDBv2.Model.TransactionCanceledException("Conditional check failed.");
        _orderRepositoryMock
            .Setup(repository => repository.SubmitGroupReservationAtomicallyAsync(
                It.IsAny<Order>(),
                It.IsAny<GroupReservation>(),
                It.IsAny<OrderStatus>(),
                It.IsAny<GroupReservationStatus>(),
                It.IsAny<long?>()))
            .ThrowsAsync(transactionFailure);

        Amazon.DynamoDBv2.Model.TransactionCanceledException exception =
            await Assert.ThrowsAsync<Amazon.DynamoDBv2.Model.TransactionCanceledException>(
                () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));

        Assert.Same(transactionFailure, exception);
        _groupReservationRepositoryMock.Verify(
            repository => repository.GetGroupReservationByIdAsync("group-1"), Times.Exactly(2));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_GroupReservationNotFound_ThrowsNotFound()
    {
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync("group-1"))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(
            () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));

        _reservationRepositoryMock.Verify(
            repository => repository.GetReservationByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_ReservationNotFound_ThrowsNotFound()
    {
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync("group-1"))
            .ReturnsAsync(new GroupReservation
            {
                GroupReservationId = "group-1",
                ReservationId = "reservation-1",
                ReservationToken = "token-1"
            });
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync("reservation-1"))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(
            () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_NoSubmittedSessions_ThrowsBadRequestValidation()
    {
        SetupValidSubmitData([]);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));

        Assert.Equal("There are no submitted guest sessions.", exception.Message);
        _orderRepositoryMock.Verify(
            repository => repository.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_SubmittedSessionsWithoutDishes_ThrowsBadRequestValidation()
    {
        SetupValidSubmitData(
        [
            new GuestSession
            {
                Id = "session-1",
                GroupReservationId = "group-1",
                GuestName = "Guest",
                IsSubmitted = true,
                Dishes = []
            }
        ]);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));

        Assert.Equal("Submitted guest sessions contain no dishes.", exception.Message);
        _orderRepositoryMock.Verify(
            repository => repository.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_OrderNotFound_ThrowsBadRequestValidation()
    {
        SetupValidSubmitData(
        [
            new GuestSession
            {
                Id = "session-1",
                GroupReservationId = "group-1",
                GuestName = "Guest",
                IsSubmitted = true,
                Dishes = [new OrderItem { DishId = "dish-1", Quantity = 1 }]
            }
        ]);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync("reservation-1"))
            .ReturnsAsync((Order?)null);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => _service.SubmitGroupReservationAsync("owner-user", "group-1", "reservation-1"));

        Assert.Equal("Order was not found for the reservation.", exception.Message);
        _orderRepositoryMock.Verify(
            repository => repository.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task SubmitGroupReservationAsync_NonOwner_ThrowsForbiddenWithoutLoadingSessions()
    {
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync("group-1"))
            .ReturnsAsync(new GroupReservation
            {
                GroupReservationId = "group-1",
                ReservationId = "reservation-1",
                ReservationToken = "token-1"
            });
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync("reservation-1"))
            .ReturnsAsync(new Reservation
            {
                Id = "reservation-1",
                TableId = "table-1",
                CustomerId = "actual-owner",
                WaiterId = "waiter-1"
            });

        await Assert.ThrowsAsync<ActionForbiddenException>(
            () => _service.SubmitGroupReservationAsync("other-user", "group-1", "reservation-1"));

        _guestSessionRepositoryMock.Verify(
            repository => repository.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        _orderRepositoryMock.Verify(
            repository => repository.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    private void SetupValidSubmitData(
        List<GuestSession> sessions,
        GroupReservationStatus groupReservationStatus = GroupReservationStatus.Open,
        bool setupGroupReservation = true)
    {
        if (setupGroupReservation)
        {
            _groupReservationRepositoryMock
                .Setup(repository => repository.GetGroupReservationByIdAsync("group-1"))
                .ReturnsAsync(CreateGroupReservation(groupReservationStatus));
        }

        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync("reservation-1"))
            .ReturnsAsync(new Reservation
            {
                Id = "reservation-1",
                TableId = "table-1",
                CustomerId = "owner-user",
                WaiterId = "waiter-1"
            });
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync("group-1"))
            .ReturnsAsync(sessions);
    }

    private static GroupReservation CreateGroupReservation(GroupReservationStatus status) => new()
    {
        GroupReservationId = "group-1",
        ReservationId = "reservation-1",
        ReservationToken = "token-1",
        Status = status
    };

    private static Order CreateSubmitOrder() => new()
    {
        OrderId = "order-1",
        CustomerId = "owner-user",
        ReservationId = "reservation-1",
        ReservationAddress = "Main Street",
        ReservationTime = "7:00 PM",
        Dishes = [],
        Status = OrderStatus.Created
    };
}
