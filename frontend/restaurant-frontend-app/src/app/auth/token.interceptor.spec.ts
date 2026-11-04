import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;
  let authService: jasmine.SpyObj<AuthenticationService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });

  it('adds the owner JWT to the group sessions request', () => {
    authService.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'access-token' });

    http.get('https://test.api/guest/groups/group-1/sessions').subscribe();

    const request = httpMock.expectOne('https://test.api/guest/groups/group-1/sessions');
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    request.flush({ participants: [], total: 0 });
  });

  it('adds the owner JWT to the remove participant request', () => {
    authService.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'access-token' });

    http.delete('https://test.api/guest/groups/group-1/sessions/guest-1').subscribe();

    const request = httpMock.expectOne('https://test.api/guest/groups/group-1/sessions/guest-1');
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-id-token');
    request.flush({ participants: [], total: 0 });
  });

  it('keeps guest-session authorization handling unchanged', () => {
    authService.getTokens.and.returnValue({ idToken: 'owner-id-token', accessToken: 'access-token' });

    http.get('https://test.api/guest/sessions/guest-1/menu', {
      headers: { Authorization: 'Bearer guest-session-token' },
    }).subscribe();

    const request = httpMock.expectOne('https://test.api/guest/sessions/guest-1/menu');
    expect(request.request.headers.get('Authorization')).toBe('Bearer guest-session-token');
    request.flush({});
  });
});
