// Практичне · картка учасника. Тести готові — пиши код, поки всі не стануть зеленими.
// Запуск: npm run test:hw
import { TestBed } from '@angular/core/testing';
import { Participant } from '@app/models/group-orders/participant.model';
import { ParticipantCardComponent } from './participant-card.component';

const OLENA: Participant = {
  id: 'olena',
  type: 'Guest',
  name: 'Olena',
  isSubmitted: false,
  total: 440,
  items: [
    {
      dish: {
        id: 'dish2',
        name: 'Tom Yum',
        price: 220,
        imageUrl: 'assets/img/dishes/spring-salad.jpg',
      },
      quantity: 2,
    },
  ],
};
const OWNER: Participant = {
  id: 'owner',
  type: 'Owner',
  name: 'Yevhenii',
  isSubmitted: true,
  total: 0,
  items: [],
};

function render(participant: Participant): HTMLElement {
  const fixture = TestBed.createComponent(ParticipantCardComponent);
  fixture.componentRef.setInput('participant', participant); // так у тесті передаємо input
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('Практичне · картка учасника', () => {
  it('показує Edit my Pre-Order тільки у картці власника та віддає клік', () => {
    const fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', OWNER);
    fixture.componentRef.setInput('isOwner', true);
    const editClick = jasmine.createSpy('editClick');
    fixture.componentInstance.editClick.subscribe(editClick);
    fixture.detectChanges();

    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector('.edit-button');
    expect(button?.textContent).toContain('Edit my Pre-Order');
    button?.click();
    expect(editClick).toHaveBeenCalledTimes(1);

    expect(render(OLENA).querySelector('.edit-button')).toBeNull();
  });

  it('показує Remove Guest тільки коли дозволено та віддає клік', () => {
    const fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', OLENA);
    fixture.componentRef.setInput('canRemove', true);
    const removeClick = jasmine.createSpy('removeClick');
    fixture.componentInstance.removeClick.subscribe(removeClick);
    fixture.detectChanges();

    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector(
      'button[aria-label="Remove Olena"]'
    );
    expect(button?.textContent).toContain('Remove Guest');
    button?.click();
    expect(removeClick).toHaveBeenCalledOnceWith(OLENA);

    const ownerFixture = TestBed.createComponent(ParticipantCardComponent);
    ownerFixture.componentRef.setInput('participant', OWNER);
    ownerFixture.componentRef.setInput('isOwner', true);
    ownerFixture.componentRef.setInput('canRemove', true);
    ownerFixture.detectChanges();
    expect(
      ownerFixture.nativeElement.querySelector('button[aria-label="Remove Yevhenii"]')
    ).toBeNull();
  });

  describe('крок 1 · заголовок картки', () => {
    it('гість: <h2> «Guest: Olena»', () => {
      expect(render(OLENA).querySelector('h2')?.textContent).toContain('Guest: Olena');
    });

    it('власник: <h2> «Owner: Yevhenii»', () => {
      expect(render(OWNER).querySelector('h2')?.textContent).toContain('Owner: Yevhenii');
    });
  });

  describe('крок 3 · страви і Subtotal', () => {
    it('показує назву страви, кількість і ціну × кількість через currency', () => {
      const text = render(OLENA).textContent ?? '';
      expect(text).toContain('Tom Yum');
      expect(text).toContain('2');
      expect(text).toContain('$440.00');
    });

    it('показує Subtotal учасника', () => {
      expect(render(OLENA).textContent).toContain('Subtotal');
    });

    it('порожній кошик: «No dishes yet»', () => {
      expect(render(OWNER).textContent).toContain('No dishes yet');
    });

    it('фото страви має розмір 40×40', () => {
      const img = render(OLENA).querySelector('img.avatar');
      expect(img?.getAttribute('width')).toBe('40');
      expect(img?.getAttribute('height')).toBe('40');
    });
  });

  it('показує перед кожним аватаром кнопку з іконкою кошика і передає страву при кліку', () => {
    const participant: Participant = {
      ...OLENA,
      items: [...OLENA.items, { dish: { id: 'dish3', name: 'Salad', price: 10 }, quantity: 1 }],
    };
    const fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', participant);
    const removeDishClick = jasmine.createSpy('removeDishClick');
    fixture.componentInstance.removeDishClick.subscribe(removeDishClick);
    fixture.detectChanges();

    const rows = [...fixture.nativeElement.querySelectorAll('.item')] as HTMLElement[];
    expect(rows.length).toBe(2);
    for (const row of rows) {
      const button = row.querySelector('.remove-dish-button');
      const avatar = row.querySelector('.avatar');
      expect(button).not.toBeNull();
      expect(avatar).not.toBeNull();
      expect(
        button!.compareDocumentPosition(avatar!) & Node.DOCUMENT_POSITION_FOLLOWING
      ).toBeTruthy();
      expect(row.querySelector('.remove-dish-button img')?.getAttribute('src')).toBe(
        'assets/svg/trash.svg'
      );
    }

    const button = rows[0].querySelector<HTMLButtonElement>('.remove-dish-button');
    button?.click();
    expect(removeDishClick).toHaveBeenCalledOnceWith(participant.items[0]);
  });

  it('показує кнопки зміни кількості розміром 32px і емітить вибрану страву', () => {
    const fixture = TestBed.createComponent(ParticipantCardComponent);
    fixture.componentRef.setInput('participant', OLENA);
    const decreaseQuantity = jasmine.createSpy('decreaseQuantity');
    const increaseQuantity = jasmine.createSpy('increaseQuantity');
    fixture.componentInstance.decreaseQuantity.subscribe(decreaseQuantity);
    fixture.componentInstance.increaseQuantity.subscribe(increaseQuantity);
    fixture.detectChanges();

    const quantity = fixture.nativeElement.querySelector('.qty') as HTMLElement;
    const buttons = [...quantity.querySelectorAll('button')] as HTMLButtonElement[];
    expect(buttons.map((button) => button.textContent?.trim())).toEqual(['-', '+']);
    expect(buttons[0].getBoundingClientRect().width).toBe(32);
    expect(buttons[0].getBoundingClientRect().height).toBe(32);
    expect(quantity.textContent).toContain('2');

    buttons[0].click();
    buttons[1].click();
    expect(decreaseQuantity).toHaveBeenCalledOnceWith(OLENA.items[0]);
    expect(increaseQuantity).toHaveBeenCalledOnceWith(OLENA.items[0]);
  });

  it('не показує статус Submitted або Still choosing у картці', () => {
    expect(render(OLENA).textContent).not.toContain('Still choosing');
    expect(render({ ...OLENA, isSubmitted: true }).textContent).not.toContain('Submitted');
  });
});
