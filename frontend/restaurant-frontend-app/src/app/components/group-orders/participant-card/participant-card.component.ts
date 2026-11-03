import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Participant, ParticipantItem } from '@app/models/group-orders/participant.model';

@Component({
  selector: 'app-participant-card',
  templateUrl: './participant-card.component.html',
  imports: [CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './participant-card.component.scss',
})
export class ParticipantCardComponent {
  // що картка отримує ззовні — як параметр функції
  readonly participant = input.required<Participant>();
  readonly isOwner = input(false);
  readonly isReadOnly = input(false);
  readonly editClick = output<void>();
  readonly canRemove = input(false);
  readonly removeClick = output<void>();
  readonly removeDishClick = output<ParticipantItem>();
  readonly decreaseQuantity = output<ParticipantItem>();
  readonly increaseQuantity = output<ParticipantItem>();

  // рахується саме, коли змінюється participant
  readonly title = computed(() => {
    const person = this.participant();
    if (this.isOwner()) {
      return 'My Pre-Order';
    }

    return person.type === 'Owner' ? `Owner: ${person.name}` : `Guest: ${person.name}`;
  });
}
