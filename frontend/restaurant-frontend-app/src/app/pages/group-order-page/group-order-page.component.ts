import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { EMPTY, catchError, finalize, forkJoin, map, switchMap, tap } from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { GroupStatusResponse, GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ReservationService } from '@app/services/reservation/reservation.service';
import { ToastService } from '@app/services/toast/toast.service';

@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, ModalComponent, ParticipantCardComponent],
  templateUrl: './group-order-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './group-order-page.component.scss',
})
export class GroupOrderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly groupOrdersService = inject(GroupOrdersService);
  private readonly reservationService = inject(ReservationService);
  private readonly toast = inject(ToastService);
  private readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  readonly invite = signal<GuestInvite | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly ownerSessionId = signal('');
  readonly groupStatus = signal<GroupReservationStatus>('Open');
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly isSubmitting = signal(false);
  readonly isConfirmOpen = signal(false);
  readonly consolidatedOrder = computed(() => consolidatePreOrder(this.participants()));
  readonly pendingParticipantsCount = computed(
    () => this.participants().filter((participant) => participant.type === 'Guest' && !participant.isSubmitted).length,
  );
  readonly hasSubmittedGuest = computed(
    () => this.participants().some((participant) => participant.type === 'Guest' && participant.isSubmitted),
  );
  readonly hasAnyDishes = computed(
    () => this.participants().some((participant) => participant.items.length > 0),
  );

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    if (!this.reservationId) {
      this.loading.set(false);
      this.loadError.set('Group reservation not found');
      return;
    }

    this.loading.set(true);
    this.loadError.set('');

    forkJoin({
      invite: this.groupOrdersService.getGroupInvite$(this.reservationId),
      ownerSessionId: this.groupOrdersService.getOwnerSessionId$(this.reservationId),
    })
      .pipe(
        switchMap(({ invite, ownerSessionId }) =>
          forkJoin({
            sessions: this.groupOrdersService.getSessions$(invite.GroupReservationId),
            status: this.groupOrdersService.getStatus$(invite.GroupReservationId),
          }).pipe(map(({ sessions, status }) => ({ invite, ownerSessionId, sessions, status }))),
        ),
        tap(({ invite, ownerSessionId, sessions, status }) => {
          this.invite.set(invite);
          this.ownerSessionId.set(ownerSessionId);
          this.participants.set(sessions.participants);
          this.groupStatus.set(status.status);
        }),
        catchError((error: unknown) => {
          this.loadError.set(
            error instanceof HttpErrorResponse && error.status === 404
              ? 'Group reservation not found'
              : 'Unable to load group reservation. Please try again.',
          );
          return EMPTY;
        }),
        finalize(() => this.loading.set(false)),
      )
      .subscribe();
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
    if (!this.isSubmitting() && this.hasSubmittedGuest() && this.consolidatedOrder().items.length > 0) {
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
          this.groupStatus.set('Submitted');
          this.isConfirmOpen.set(false);
          this.toast.success('Pre-order sent to the restaurant');
        }),
        catchError(() => EMPTY),
        finalize(() => this.isSubmitting.set(false)),
      )
      .subscribe();
  }
}
