import { fakeAsync, TestBed, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ReservationService } from '@app/services/reservation/reservation.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const GROUP_ID = 'group-1';
const SESSIONS: GroupSessionsResponse = {
  participants: [
    {
      id: 'guest-1',
      type: 'Guest',
      name: 'Andrii',
      isSubmitted: true,
      total: 180,
      items: [{ dish: { id: 'dish-1', name: 'Caesar Salad', price: 180 }, quantity: 1 }],
    },
  ],
  total: 180,
};
const STATUS_OPEN = { currentParticipants: 2, maxCapacity: 5, status: 'Open' as const };
const INVITE: GuestInvite = {
  GroupReservationId: GROUP_ID,
  LocationId: 'location-1',
  RestaurantName: 'Restaurant',
  LocationAddress: 'Rustaveli Avenue',
  StartTime: '2026-11-04T12:00:00Z',
  EndTime: '2026-11-04T13:00:00Z',
  MaxGuestCount: 5,
  TableNumber: 1,
  InviterName: 'Owner',
};

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === text
  ) as HTMLButtonElement | undefined;
}

describe('GroupOrderPageComponent background sync', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  let fixture: ReturnType<typeof createFixture>;

  function createFixture() {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        {
          provide: ReservationService,
          useValue: jasmine.createSpyObj('ReservationService', ['getReservationById']),
        },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
        {
          provide: ToastService,
          useValue: jasmine.createSpyObj('ToastService', ['success', 'error', 'info']),
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ reservationId: 'reservation-1' }) },
          },
        },
      ],
    });
    const componentFixture = TestBed.createComponent(GroupOrderPageComponent);
    componentFixture.detectChanges();
    return { componentFixture, element: componentFixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$',
      'getSessions$',
      'getOwnerSessionId$',
      'getStatus$',
      'submit$',
    ]);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.returnValue(of(SESSIONS));
    service.getOwnerSessionId$.and.returnValue(of('owner-1'));
    service.getStatus$.and.returnValue(of(STATUS_OPEN));
    service.submit$.and.returnValue(of([]));
  });

  afterEach(() => fixture?.componentFixture.destroy());

  it('refreshes sessions and status every 15 seconds using silent requests', fakeAsync(() => {
    fixture = createFixture();
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
    expect(fixture.element.textContent).toContain('Updated at');

    tick(14_999);
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
    tick(1);

    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    expect(service.getSessions$.calls.mostRecent().args).toEqual([GROUP_ID, { silent: true }]);
    expect(service.getStatus$.calls.mostRecent().args).toEqual([GROUP_ID, { silent: true }]);
  }));

  it('refreshes immediately from the Refresh button', fakeAsync(() => {
    fixture = createFixture();

    buttonByText(fixture.element, 'Refresh')!.click();

    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    expect(service.getSessions$.calls.mostRecent().args).toEqual([GROUP_ID, { silent: true }]);
    expect(fixture.element.textContent).not.toContain("Couldn't refresh.");
  }));

  it('keeps the previous snapshot and shows its timestamp when refresh fails', fakeAsync(() => {
    service.getSessions$.and.returnValues(
      of(SESSIONS),
      throwError(() => new Error('offline'))
    );
    fixture = createFixture();
    const originalTimestamp = fixture.componentFixture.componentInstance.updatedAt();

    tick(15_000);
    fixture.componentFixture.detectChanges();

    expect(fixture.element.textContent).toContain('Andrii');
    expect(fixture.componentFixture.componentInstance.updatedAt()).toBe(originalTimestamp);
    expect(fixture.element.textContent).toContain("Couldn't refresh. Showing data from");
  }));

  it('does not start another refresh while the previous one is pending', fakeAsync(() => {
    const sessionsRefresh = new Subject<GroupSessionsResponse>();
    const statusRefresh = new Subject<typeof STATUS_OPEN>();
    service.getSessions$.and.returnValues(of(SESSIONS), sessionsRefresh, of(SESSIONS));
    service.getStatus$.and.returnValues(of(STATUS_OPEN), statusRefresh, of(STATUS_OPEN));
    fixture = createFixture();

    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);

    sessionsRefresh.next(SESSIONS);
    sessionsRefresh.complete();
    statusRefresh.next(STATUS_OPEN);
    statusRefresh.complete();
    tick(15_000);

    expect(service.getSessions$).toHaveBeenCalledTimes(3);
  }));

  it('restarts polling only once after Try again succeeds', fakeAsync(() => {
    service.getGroupInvite$.and.returnValues(
      throwError(() => new Error('offline')),
      of(INVITE)
    );
    fixture = createFixture();

    buttonByText(fixture.element, 'Try again')!.click();
    fixture.componentFixture.detectChanges();
    expect(service.getSessions$).toHaveBeenCalledTimes(1);

    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(2);
  }));

  it('stops polling after submit', fakeAsync(() => {
    fixture = createFixture();
    buttonByText(fixture.element, 'Submit Pre-Order')!.click();
    fixture.componentFixture.detectChanges();
    buttonByText(fixture.element, 'Submit')!.click();
    tick();

    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
  }));

  it('cleans up the polling timer when the page is destroyed', fakeAsync(() => {
    fixture = createFixture();
    fixture.componentFixture.destroy();
    tick(15_000);
    expect(service.getSessions$).toHaveBeenCalledTimes(1);
  }));
});
