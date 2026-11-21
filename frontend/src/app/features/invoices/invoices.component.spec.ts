import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../auth/services/auth.service';
import { ReservationRealtimeService } from '../reservations/services/reservation-realtime.service';
import { Invoice, InvoicesComponent } from './invoices.component';

describe('InvoicesComponent', () => {
  let fixture: ComponentFixture<InvoicesComponent>;
  let component: InvoicesComponent;
  let httpTesting: HttpTestingController;

  const mockInvoices: Invoice[] = [
    {
      id: 'inv-1',
      reservationCode: 'RES-ALPHA-01',
      totalKwh: 20,
      kwhRate: 3858,
      swapRate: null,
      totalAmount: 77160,
      status: 'Paid',
      paymentReference: 'PAY-REF-01',
      paymentMethod: 'Cash',
      collectedByOperatorId: 'OP-01',
      dueAtUtc: null,
      paidAtUtc: '2026-11-21T06:26:00Z',
    },
    {
      id: 'inv-2',
      reservationCode: 'RES-BETA-02',
      totalKwh: 10,
      kwhRate: 3858,
      swapRate: null,
      totalAmount: 38580,
      status: 'Pending',
      paymentReference: null,
      paymentMethod: null,
      collectedByOperatorId: null,
      dueAtUtc: '2026-11-28T06:26:00Z',
      paidAtUtc: null,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InvoicesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isAuthenticated: () => true,
            currentUser: () => ({ fullName: 'Trần Khách Hàng', roles: ['Customer'] }),
            home: () => ({ path: '/', label: 'Bản đồ trạm' }),
          },
        },
        {
          provide: ReservationRealtimeService,
          useValue: {
            invoiceChanged$: of(),
          },
        },
      ],
    }).compileComponents();

    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InvoicesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const req = httpTesting.expectOne((r) => r.url === '/api/invoices');
    req.flush(mockInvoices);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('renders invoice statistics accurately', () => {
    expect(component.totalPaidAmount()).toBe(77160);
    expect(component.totalPendingAmount()).toBe(38580);
    expect(component.totalKwhCharged()).toBe(30);
    expect(component.paidCount()).toBe(1);
    expect(component.pendingCount()).toBe(1);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('77,160');
    expect(el.textContent).toContain('38,580');
    expect(el.textContent).toContain('RES-ALPHA-01');
    expect(el.textContent).toContain('RES-BETA-02');
  });

  it('filters invoices by tab', () => {
    component.setFilter('pending');
    fixture.detectChanges();
    expect(component.filteredInvoices().length).toBe(1);
    expect(component.filteredInvoices()[0].id).toBe('inv-2');

    component.setFilter('paid');
    fixture.detectChanges();
    expect(component.filteredInvoices().length).toBe(1);
    expect(component.filteredInvoices()[0].id).toBe('inv-1');

    component.setFilter('all');
    fixture.detectChanges();
    expect(component.filteredInvoices().length).toBe(2);
  });

  it('filters invoices by search query', () => {
    component.searchQuery.set('BETA');
    fixture.detectChanges();
    expect(component.filteredInvoices().length).toBe(1);
    expect(component.filteredInvoices()[0].reservationCode).toBe('RES-BETA-02');
  });

  it('handles begin payment flow and opens QR attempt', async () => {
    component.begin('inv-2');
    const payReq = httpTesting.expectOne('/api/invoices/inv-2/pay');
    payReq.flush({
      id: 'attempt-123',
      invoiceId: 'inv-2',
      qrPayload: 'mock-qr-payload',
    });

    // Wait for promise resolving QR DataURL
    await new Promise((resolve) => setTimeout(resolve, 150));
    fixture.detectChanges();

    expect(component.attempt()).not.toBeNull();
    expect(component.attempt()?.invoiceId).toBe('inv-2');
    expect(component.activeInvoiceForQr()?.reservationCode).toBe('RES-BETA-02');

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Thanh toán qua mã QR');

    // Settle mock payment
    component.settle(true);
    const settleReq = httpTesting.expectOne('/api/payments/attempt-123/mock-result');
    expect(settleReq.request.body).toEqual({ succeeded: true, amount: undefined });
    settleReq.flush({});

    // Settle triggers refresh
    const refreshReq = httpTesting.expectOne((r) => r.url === '/api/invoices');
    refreshReq.flush([]);
  });
});
