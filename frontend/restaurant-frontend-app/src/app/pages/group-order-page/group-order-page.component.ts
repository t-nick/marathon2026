import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnDestroy,
  OnInit,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  EMPTY,
  Subscription,
  catchError,
  exhaustMap,
  finalize,
  forkJoin,
  map,
  switchMap,
  tap,
  timer,
} from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import {
  GroupStatusResponse,
  GroupReservationStatus,
} from '@app/models/group-orders/group-status.model';
import { Participant, ParticipantItem } from '@app/models/group-orders/participant.model';
import { Order } from '@app/models/orders/order.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { ReservationService } from '@app/services/reservation/reservation.service';
import { ToastService } from '@app/services/toast/toast.service';

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ModalComponent, ParticipantCardComponent],
  templateUrl: './group-order-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './group-order-page.component.scss',
})
export class GroupOrderPageComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly groupOrdersService = inject(GroupOrdersService);
  private readonly preOrdersService = inject(PreOrdersService);
  private readonly reservationService = inject(ReservationService);
  private readonly toast = inject(ToastService);
  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly groupStatus = signal<GroupReservationStatus>('Open');
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly isSubmitting = signal(false);
  readonly isConfirmOpen = signal(false);
  readonly guestToRemove = signal<Participant | null>(null);
  readonly isRemovingGuest = signal(false);
  readonly isRemovingDish = signal(false);
  readonly isRefreshing = signal(false);
  readonly updatedAt = signal<Date | null>(null);
  readonly refreshFailed = signal(false);
  readonly consolidatedOrder = computed(() => consolidatePreOrder(this.participants()));
  readonly pendingParticipantsCount = computed(
    () =>
      this.participants().filter(
        (participant) => participant.type === 'Guest' && !participant.isSubmitted
      ).length
  );
  readonly hasSubmittedGuest = computed(() =>
    this.participants().some(
      (participant) => participant.type === 'Guest' && participant.isSubmitted
    )
  );
  readonly hasAnyDishes = computed(() =>
    this.participants().some((participant) => participant.items.length > 0)
  );

  private pollingSubscription?: Subscription;

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  loadData(): void {
    this.stopPolling();

    if (!this.reservationId) {
      this.loading.set(false);
      this.loadError.set('Group reservation not found');
      return;
    }

    this.loading.set(true);
    this.loadError.set('');

    this.groupOrdersService
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.groupOrdersService.getSessions$(invite.GroupReservationId),
            status: this.groupOrdersService.getStatus$(invite.GroupReservationId),
          }).pipe(map(({ sessions, status }) => ({ invite, sessions, status })))
        ),
        tap(({ invite, sessions, status }) => {
          this.invite.set(invite);
          this.participants.set(sessions.participants);
          this.groupStatus.set(status.status);
          this.updatedAt.set(new Date());
          this.refreshFailed.set(false);
          if (status.status === 'Open') {
            this.startPolling(invite.GroupReservationId);
          }
        }),
        catchError((error: unknown) => {
          this.loadError.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'Group reservation not found'
              : 'Unable to load group reservation. Please try again.'
          );
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      )
      .subscribe();
  }

  refreshNow(): void {
    const invite = this.invite();
    if (
      !invite ||
      this.groupStatus() !== 'Open' ||
      this.isRefreshing() ||
      this.isRemovingGuest() ||
      this.isRemovingDish()
    ) {
      return;
    }

    this.refreshGroupData(invite.GroupReservationId).subscribe();
  }

  removeOwnerDish(item: ParticipantItem): void {
    const invite = this.invite();
    const owner = this.participants().find((participant) => participant.type === 'Owner');

    if (
      !invite ||
      !owner ||
      owner.type !== 'Owner' ||
      !owner.items.some((ownerItem) => ownerItem.dish.id === item.dish.id) ||
      this.groupStatus() !== 'Open' ||
      this.isRemovingDish() ||
      this.isRemovingGuest() ||
      this.isRefreshing()
    ) {
      return;
    }

    this.isRemovingDish.set(true);
    this.preOrdersService
      .getByReservationId(this.reservationId)
      .pipe(
        switchMap((order) => {
          if (!order.orderItems.some((orderItem) => orderItem.dish.id === item.dish.id)) {
            throw new Error(`Dish ${item.dish.id} was not found in the owner's reservation order.`);
          }

          const updatedOrder: Order = {
            ...order,
            orderItems: order.orderItems.filter((orderItem) => orderItem.dish.id !== item.dish.id),
          };

          return this.preOrdersService
            .editPreOrderByReservationId(this.reservationId, updatedOrder)
            .pipe(switchMap(() => this.groupOrdersService.getSessions$(invite.GroupReservationId)));
        }),
        tap((sessions) => {
          this.participants.set(sessions.participants);
          this.updatedAt.set(new Date());
          this.refreshFailed.set(false);
          this.toast.success(`${item.dish.name} was removed from your pre-order.`);
        }),
        catchError((error: unknown) => {
          console.error(`Unable to remove dish ${item.dish.id} from the owner's pre-order.`, error);
          return EMPTY;
        }),
        finalize(() => this.isRemovingDish.set(false))
      )
      .subscribe();
  }

  requestRemoveGuest(participant: Participant): void {
    if (
      participant.type === 'Guest' &&
      this.groupStatus() === 'Open' &&
      !this.isRemovingGuest() &&
      !this.isRefreshing() &&
      !this.isRemovingDish()
    ) {
      this.guestToRemove.set(participant);
    }
  }

  cancelRemoveGuest(): void {
    if (!this.isRemovingGuest()) {
      this.guestToRemove.set(null);
    }
  }

  confirmRemoveGuest(): void {
    const invite = this.invite();
    const participant = this.guestToRemove();
    if (
      !invite ||
      !participant ||
      participant.type !== 'Guest' ||
      this.groupStatus() !== 'Open' ||
      this.isRemovingGuest() ||
      this.isRefreshing() ||
      this.isRemovingDish()
    ) {
      return;
    }

    this.isRemovingGuest.set(true);
    this.groupOrdersService
      .removeSession$(invite.GroupReservationId, participant.id)
      .pipe(
        tap((sessions) => {
          this.participants.set(sessions.participants);
          this.updatedAt.set(new Date());
          this.refreshFailed.set(false);
          this.guestToRemove.set(null);
          this.toast.success(`${participant.name} was removed from the group.`);
        }),
        catchError(() => EMPTY),
        finalize(() => this.isRemovingGuest.set(false))
      )
      .subscribe();
  }

  private startPolling(groupReservationId: string): void {
    this.stopPolling();
    this.pollingSubscription = timer(15_000, 15_000)
      .pipe(exhaustMap(() => this.refreshGroupData(groupReservationId)))
      .subscribe();
  }

  private stopPolling(): void {
    this.pollingSubscription?.unsubscribe();
    this.pollingSubscription = undefined;
  }

  private refreshGroupData(groupReservationId: string) {
    if (
      this.isRefreshing() ||
      this.isRemovingGuest() ||
      this.isRemovingDish() ||
      this.groupStatus() !== 'Open'
    ) {
      return EMPTY;
    }

    this.isRefreshing.set(true);
    return forkJoin({
      sessions: this.groupOrdersService.getSessions$(groupReservationId, { silent: true }),
      status: this.groupOrdersService.getStatus$(groupReservationId, { silent: true }),
    }).pipe(
      tap(({ sessions, status }) => {
        this.participants.set(sessions.participants);
        this.groupStatus.set(status.status);
        this.updatedAt.set(new Date());
        this.refreshFailed.set(false);
        if (status.status !== 'Open') {
          this.stopPolling();
        }
      }),
      catchError(() => {
        this.refreshFailed.set(true);
        return EMPTY;
      }),
      finalize(() => this.isRefreshing.set(false))
    );
  }

  editOwnerPreOrder(): void {
    this.reservationService.getReservationById(this.reservationId).subscribe({
      next: (reservation) => {
        const startTime = new Date(reservation.startTime);
        const endTime = new Date(reservation.endTime);
        const tableLabel = reservation.tableName || `Table ${reservation.tableId}`;

        void this.router.navigate(['/menu'], {
          queryParams: {
            preOrder: 'true',
            reservationId: reservation.id,
            reservationLabel: [
              reservation.locationAddress,
              tableLabel,
              startTime.toDateString(),
              `${startTime.toLocaleTimeString()} - ${endTime.toLocaleTimeString()}`,
            ].join(', '),
          },
        });
      },
      error: (error: unknown) => {
        console.error('Unable to load reservation details for editing the pre-order.', error);
      },
    });
  }

  openSubmitConfirmation(): void {
    if (
      !this.isSubmitting() &&
      !this.isRemovingDish() &&
      this.hasSubmittedGuest() &&
      this.consolidatedOrder().items.length > 0
    ) {
      this.isConfirmOpen.set(true);
    }
  }

  closeSubmitConfirmation(): void {
    if (!this.isSubmitting()) {
      this.isConfirmOpen.set(false);
    }
  }

  submitPreOrder(): void {
    const invite = this.invite();
    if (
      !invite ||
      this.isSubmitting() ||
      this.isRemovingDish() ||
      this.groupStatus() !== 'Open' ||
      !this.hasSubmittedGuest() ||
      this.consolidatedOrder().items.length === 0
    ) {
      return;
    }

    this.isSubmitting.set(true);
    this.groupOrdersService
      .submit$(invite.GroupReservationId, this.reservationId)
      .pipe(
        tap(() => {
          this.stopPolling();
          this.groupStatus.set('Submitted');
          this.isConfirmOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        }),
        catchError(() => EMPTY),
        finalize(() => this.isSubmitting.set(false))
      )
      .subscribe();
  }
}
