import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../auth/services/auth.service';
import { NotificationsComponent } from './notifications.component';

describe('NotificationsComponent', () => {
  it('shows the signed-in customer the stored incident notification', async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: {
          isAuthenticated: () => true, currentUser: () => null,
          home: () => ({ path: '/', label: 'Bản đồ trạm' }),
        } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationsComponent);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/notifications').flush({
      totalCount: 21,
      pageSize: 20,
      items: [
        {
          id: 'incident-notification',
          title: 'Lượt đặt đã hủy do sự cố',
          message: 'Trạm không còn tài nguyên thay thế.',
          createdAtUtc: '2026-11-21T02:00:00Z',
        },
      ],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Lượt đặt đã hủy do sự cố');
    expect(fixture.nativeElement.textContent).toContain('Trạm không còn tài nguyên thay thế.');
    const nextButton = (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('.notice-pagination button')[1];
    nextButton.click();
    http.expectOne('/api/notifications?page=2').flush({ items: [], totalCount: 21, pageSize: 20 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Trang 2 / 2');
    http.verify();
  });
});
