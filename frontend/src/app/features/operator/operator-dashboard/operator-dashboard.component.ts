import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  BatteryLifecycleState,
  ChargerEquipmentState,
  OperatorActiveReservation,
  OperatorBattery,
  OperatorChargingBay,
  ParkingOccupancyState,
  countBookableResources,
} from '../models/operator.models';
import { OperatorService } from '../services/operator.service';
import { EMPTY, auditTime, catchError, filter, finalize, merge, switchMap, tap, timer } from 'rxjs';
import { extractApiError } from '../../../core/utils/api-error.util';
import { DialogService } from '../../../core/services/dialog.service';
import { AuthService } from '../../auth/services/auth.service';
import { ReservationRealtimeService } from '../../reservations/services/reservation-realtime.service';

import { InvoicesComponent } from '../../invoices/invoices.component';
import { StationPricingComponent } from './station-pricing.component';
import { ReservationIncidentDialogComponent } from './reservation-incident-dialog.component';
import { NotificationBellComponent } from '../../notifications/notification-bell.component';

type DashboardTab = 'operations' | 'bays' | 'batteries' | 'checkout';

@Component({
  selector: 'app-operator-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, InvoicesComponent, StationPricingComponent,
    ReservationIncidentDialogComponent, NotificationBellComponent],
  templateUrl: './operator-dashboard.component.html',
  styleUrls: ['./operator-dashboard.component.scss'],
})
export class OperatorDashboardComponent implements OnInit {
  private readonly operatorService = inject(OperatorService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly realtime = inject(ReservationRealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(DialogService);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.meterModalOpen()) {
      this.closeMeterModal();
    } else if (this.swapReservation() && !this.billingBusy()) {
      this.swapReservation.set(null);
    }
  }

  readonly currentTab = signal<DashboardTab>('operations');
  readonly checkoutInvoiceId = signal<string | null>(null);
  readonly focusedReservationId = signal<string | null>(null);
  readonly orderedReservations = computed(() => {
    const focus = this.focusedReservationId();
    const visits = this.stationData()?.reservations ?? [];
    return focus ? [...visits.filter((visit) => visit.id === focus), ...visits.filter((visit) => visit.id !== focus)] : visits;
  });
  readonly stationData = this.operatorService.stationData;
  readonly loading = this.operatorService.loading;
  readonly error = this.operatorService.error;
  readonly currentUser = this.authService.currentUser;
  readonly swapReservation = signal<OperatorActiveReservation | null>(null);
  readonly billingError = signal<string | null>(null);
  readonly billingBusy = signal(false);
  readonly returnedSerial = signal('');
  readonly issuedSerial = signal('');
  readonly inspectionResult = signal('');
  readonly faultTarget = signal<{ kind: 'bay' | 'battery'; id: string; code: string } | null>(null);
  readonly faultDescription = signal('');
  readonly faultBusy = signal(false);
  readonly faultError = signal<string | null>(null);
  private readonly faultDialog = viewChild.required<ElementRef<HTMLDialogElement>>('faultDialog');
  readonly incidentNotice = signal<string | null>(null);

  // Quick check-in input
  readonly checkinCode = signal<string>('');
  readonly checkinLoading = signal<boolean>(false);
  readonly checkinError = signal<string | null>(null);
  readonly checkinSuccess = signal<string | null>(null);

  // Meter modal state
  readonly meterModalOpen = signal<boolean>(false);
  readonly meterModalMode = signal<'start' | 'complete'>('start');
  readonly selectedReservation = signal<OperatorActiveReservation | null>(null);
  readonly meterKwhInput = signal<number | null>(null);
  readonly meterSubmitting = signal<boolean>(false);
  readonly confirmingArrival = signal<string | null>(null);
  readonly meterError = signal<string | null>(null);

  // Battery filter & update state
  readonly batteryFilter = signal<number | 'all'>('all');
  readonly updatingBatteryId = signal<string | null>(null);

  // Computed summary metrics
  readonly totalBays = computed(() => this.stationData()?.bays.length ?? 0);
  private readonly bookable = computed(() => countBookableResources(this.stationData()));
  readonly availableBays = computed(() => this.bookable().bays);
  readonly occupiedBays = computed(
    () => this.stationData()?.bays.filter((b) => b.parkingState === ParkingOccupancyState.Occupied).length ?? 0
  );
  readonly faultedBays = computed(
    () => this.stationData()?.bays.filter((b) => b.equipmentState === ChargerEquipmentState.Faulted).length ?? 0
  );

  readonly totalBatteries = computed(() => this.stationData()?.batteries.length ?? 0);
  readonly readyBatteries = computed(() => this.bookable().batteries);
  readonly chargingBatteries = computed(
    () => this.stationData()?.batteries.filter((b) => b.state === BatteryLifecycleState.Charging).length ?? 0
  );
  readonly inspectionBatteries = computed(
    () => this.stationData()?.batteries.filter((b) => b.state === BatteryLifecycleState.Inspection).length ?? 0
  );
  readonly faultedBatteries = computed(
    () => this.stationData()?.batteries.filter((b) => b.state === BatteryLifecycleState.Faulted).length ?? 0
  );

  readonly heldReservationsCount = computed(
    () => (this.stationData()?.reservations || []).filter((r) => r.status === 'Held').length
  );
  readonly checkedInReservationsCount = computed(
    () => (this.stationData()?.reservations || []).filter((r) => r.status === 'CheckedIn').length
  );

  readonly filteredBatteries = computed(() => {
    const all = this.stationData()?.batteries || [];
    const filter = this.batteryFilter();
    if (filter === 'all') return all;
    return all.filter((b) => b.state === filter);
  });

  readonly customerBatteries = computed(() => {
    return (this.stationData()?.batteries || []).filter((b) => b.state === BatteryLifecycleState.WithCustomer);
  });

  readonly isSerialConflict = computed(() => {
    const ret = this.returnedSerial().trim();
    const iss = this.issuedSerial().trim();
    return ret.length > 0 && iss.length > 0 && ret.toLowerCase() === iss.toLowerCase();
  });

  ngOnInit(): void {
    if (!this.authService.isOperator()) {
      this.router.navigate(['/']);
      return;
    }
    this.refresh();
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const invoice = params.get('invoice');
      const reservation = params.get('reservation');
      this.focusedReservationId.set(reservation);
      if (invoice) this.openCheckout(invoice);
      else if (reservation) this.currentTab.set('operations');
    });
    merge(this.realtime.operatorChanged$, timer(12000, 12000)).pipe(
      auditTime(150),
      filter(() => this.authService.isOperator() && !this.loading()),
      switchMap(() => this.operatorService.getStationDashboard(true).pipe(catchError(() => EMPTY))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe();
  }

  refresh(): void {
    this.operatorService.getStationDashboard().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: () => {},
    });
  }

  onIncidentResolved(message: string): void {
    this.incidentNotice.set(message);
    this.refresh();
  }

  openCheckout(invoiceId: string | null = null): void {
    this.checkoutInvoiceId.set(invoiceId);
    this.currentTab.set('checkout');
  }

  openSwap(reservation: OperatorActiveReservation): void {
    this.billingError.set(null);
    this.returnedSerial.set('');
    const heldSerial = reservation.batteryId ? this.findBatterySerial(reservation.batteryId) : '';
    this.issuedSerial.set(heldSerial);
    this.inspectionResult.set('Pin nguyên vẹn, tiếp điểm sạch, sẵn sàng sạc lại');
    this.swapReservation.set(reservation);
  }

  setInspection(result: string): void {
    this.inspectionResult.set(result);
  }

  completeSwap(): void {
    const reservation = this.swapReservation();
    const returned = this.returnedSerial().trim().toUpperCase();
    const issued = this.issuedSerial().trim().toUpperCase();
    const inspection = this.inspectionResult().trim();
    if (!reservation || this.billingBusy() || !returned || !issued || !inspection) return;
    if (returned === issued) {
      this.billingError.set('Mã pin khách trả và pin trạm giao phải là 2 viên pin khác nhau.');
      return;
    }
    this.billingBusy.set(true);
    this.billingError.set(null);
    this.operatorService.completeBatterySwap(reservation.id, returned, issued, inspection)
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.billingBusy.set(false))).subscribe({
        next: ({ invoice }) => {
          this.swapReservation.set(null);
          this.openCheckout(invoice.id);
          this.refresh();
        },
        error: (err: unknown) => {
          const detail = extractApiError(err, '');
          if (detail.includes('distinct')) {
            this.billingError.set('Mã pin khách trả và pin trạm giao phải là 2 viên pin khác nhau.');
          } else if (detail.includes('custody')) {
            this.billingError.set('Viên pin trả không thuộc quyền sử dụng của khách hàng này.');
          } else if (detail.includes('ready')) {
            this.billingError.set('Viên pin trạm giao phải ở trạng thái sẵn sàng trong kho trạm.');
          } else if (detail.includes('tariff')) {
            this.billingError.set('Trạm chưa cấu hình biểu giá dịch vụ đổi pin.');
          } else {
            this.billingError.set(detail || 'Không thể xác nhận giao nhận pin.');
          }
        },
      });
  }

  private openFaultForm(kind: 'bay' | 'battery', id: string, code: string): void {
    this.incidentNotice.set(null);
    this.faultTarget.set({ kind, id, code });
    this.faultError.set(null);
    this.faultDescription.set('');
    this.faultDialog().nativeElement.showModal();
  }

  lockFaultDialogWhileSaving(event: Event): void {
    if (this.faultBusy()) event.preventDefault();
  }

  reportFault(): void {
    const target = this.faultTarget();
    const description = this.faultDescription().trim();
    if (!target || !description || description.length > 500 || this.faultBusy()) return;
    this.faultBusy.set(true);
    this.faultError.set(null);
    const request = target.kind === 'bay'
      ? this.operatorService.updateBayEquipmentState(target.id, ChargerEquipmentState.Faulted, description)
      : this.operatorService.updateBatteryState(target.id, BatteryLifecycleState.Faulted, description);
    request.pipe(
      tap(() => {
        this.faultDialog().nativeElement.close();
        this.incidentNotice.set('Đã gửi báo cáo sự cố.');
      }),
      switchMap(() => this.operatorService.getStationDashboard().pipe(catchError(() => EMPTY))),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.faultBusy.set(false)),
    ).subscribe({
      next: (station) => {
        if (station.reservations.some((visit) => visit.incident &&
            (visit.chargingBayId === target.id || visit.batteryId === target.id))) {
          this.currentTab.set('operations');
        }
      },
      error: (err: unknown) => this.faultError.set(extractApiError(err, 'Không thể gửi báo cáo sự cố.')),
    });
  }

  setTab(tab: DashboardTab): void {
    this.currentTab.set(tab);
  }

  // --- Quick Check-in Logic ---
  onCodeInput(val: string): void {
    const raw = val.replace(/[^A-Za-z0-9]/g, '').toUpperCase().slice(0, 32);
    this.checkinCode.set(raw);
    this.checkinError.set(null);
    this.checkinSuccess.set(null);
  }

  formatCodeDisplay(code: string): string {
    return code.replace(/(.{4})/g, '$1 ').trim();
  }

  async pasteCode(): Promise<void> {
    try {
      const text = await navigator.clipboard.readText();
      if (text) this.onCodeInput(text);
    } catch {
      // Ignore clipboard permission denial
    }
  }

  executeQuickCheckin(): void {
    const code = this.checkinCode().trim();
    const location = this.stationData()?.location;
    if (!code || code.length < 32 || !location) {
      this.checkinError.set('Vui lòng nhập đủ mã check-in 32 ký tự.');
      return;
    }

    this.checkinLoading.set(true);
    this.checkinError.set(null);
    this.checkinSuccess.set(null);

    this.operatorService
      .checkIn({ locationId: location.id, code })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.checkinLoading.set(false);
          this.checkinCode.set('');
          this.checkinSuccess.set(`Check-in thành công cho khách hàng! Mã: ${res.code ? res.code.slice(0, 8) : ''}...`);
          this.currentTab.set('operations');
        },
        error: (err: unknown) => {
          this.checkinLoading.set(false);
          const status = (err as { status?: number })?.status;
          if (status === 404) {
            this.checkinError.set('Không tìm thấy lượt đặt hoặc mã check-in không đúng.');
          } else if (status === 409) {
            this.checkinError.set(extractApiError(err, 'Mã check-in không thuộc trạm này hoặc đã hết hạn.'));
          } else {
            this.checkinError.set(extractApiError(err, 'Lỗi xử lý check-in. Vui lòng kiểm tra lại.'));
          }
        },
      });
  }

  // --- Charging Session Stepper Logic ---
  readonly actionBusy = signal(false);

  onMeterInput(event: Event): void {
    const raw = (event.target as HTMLInputElement | null)?.value ?? '';
    if (raw.trim() === '') {
      this.meterKwhInput.set(null);
      return;
    }
    const parsed = Number(raw);
    this.meterKwhInput.set(Number.isFinite(parsed) ? parsed : null);
  }

  isValidMeterInput(): boolean {
    const val = this.meterKwhInput();
    return val !== null && Number.isFinite(val) && val >= 0;
  }

  confirmVehicleArrived(res: OperatorActiveReservation): void {
    if (this.confirmingArrival()) return;
    this.confirmingArrival.set(res.id);
    this.operatorService.confirmVehicleArrived(res.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.confirmingArrival.set(null)),
    ).subscribe({
      error: (err: unknown) => {
        if ((err as { status?: number })?.status === 409) this.refresh();
        void this.dialog.alert(extractApiError(err, 'Không thể xác nhận xe vào bãi.'), {
          title: 'Không thể thực hiện',
          tone: 'error',
        });
      },
    });
  }

  openStartChargingModal(res: OperatorActiveReservation): void {
    this.selectedReservation.set(res);
    this.meterModalMode.set('start');
    this.meterKwhInput.set(null);
    this.meterError.set(null);
    this.meterModalOpen.set(true);
  }

  openCompleteChargingModal(res: OperatorActiveReservation): void {
    this.selectedReservation.set(res);
    this.meterModalMode.set('complete');
    this.meterKwhInput.set(res.session?.startMeterKwh != null ? Number(res.session.startMeterKwh) : null);
    this.meterError.set(null);
    this.meterModalOpen.set(true);
  }

  closeMeterModal(): void {
    this.meterModalOpen.set(false);
    this.selectedReservation.set(null);
    this.meterKwhInput.set(null);
  }

  submitMeterReading(): void {
    const res = this.selectedReservation();
    const meter = this.meterKwhInput();
    if (!res || meter === null || !Number.isFinite(meter) || meter < 0) {
      this.meterError.set('Vui lòng nhập chỉ số công tơ điện hợp lệ (>= 0).');
      return;
    }

    if (this.meterModalMode() === 'complete') {
      const start = res.session?.startMeterKwh != null ? Number(res.session.startMeterKwh) : 0;
      if (meter < start) {
        this.meterError.set(`Chỉ số cuối (${meter} kWh) không được nhỏ hơn chỉ số đầu (${start} kWh).`);
        return;
      }
    }

    this.meterSubmitting.set(true);
    this.meterError.set(null);

    if (this.meterModalMode() === 'start') {
      this.operatorService.startCharging(res.id, meter).pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.meterSubmitting.set(false)),
      ).subscribe({
        next: () => this.closeMeterModal(),
        error: (err: unknown) => {
          this.meterError.set(extractApiError(err, 'Lỗi khi cập nhật chỉ số công tơ.'));
        },
      });
    } else {
      this.operatorService.completeCharging(res.id, meter).pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.meterSubmitting.set(false)),
      ).subscribe({
        next: (invoice) => {
          this.closeMeterModal();
          this.openCheckout(invoice.id);
          this.refresh();
        },
        error: (err: unknown) => {
          this.meterError.set(extractApiError(err, 'Lỗi khi cập nhật chỉ số công tơ.'));
        },
      });
    }
  }

  async confirmNoCharge(res: OperatorActiveReservation): Promise<void> {
    if (this.actionBusy()) return;
    const confirmed = await this.dialog.confirm(
      'Xác nhận khách đã vào chỗ đỗ nhưng quyết định không sạc?',
      { title: 'Xác nhận khách không sạc', tone: 'warning', confirmText: 'Xác nhận không sạc' },
    );
    if (!confirmed || this.actionBusy()) return;
    this.actionBusy.set(true);
    this.operatorService.confirmNoCharge(res.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false)),
    ).subscribe({
      next: () => {},
      error: (err: unknown) => void this.dialog.alert(extractApiError(err, 'Không thể xác nhận không sạc.'), {
        title: 'Không thể thực hiện',
        tone: 'error',
      }),
    });
  }

  async confirmVehicleDeparted(res: OperatorActiveReservation): Promise<void> {
    if (this.actionBusy()) return;
    const confirmed = await this.dialog.confirm(
      'Xác nhận xe đã rời chỗ đỗ? Thao tác này sẽ giải phóng trụ sạc về trạng thái Trống.',
      { title: 'Xác nhận xe rời bãi', tone: 'primary', confirmText: 'Xác nhận rời bãi' },
    );
    if (!confirmed || this.actionBusy()) return;
    this.actionBusy.set(true);
    this.operatorService.confirmVehicleDeparted(res.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false)),
    ).subscribe({
      next: () => {},
      error: (err: unknown) => void this.dialog.alert(extractApiError(err, 'Không thể xác nhận xe rời bãi.'), {
        title: 'Không thể thực hiện',
        tone: 'error',
      }),
    });
  }

  async closeWithoutService(res: OperatorActiveReservation): Promise<void> {
    const location = this.stationData()?.location;
    if (!location || this.actionBusy()) return;
    const confirmed = await this.dialog.confirm(
      'Đóng lượt giữ chỗ này do khách không sử dụng dịch vụ?',
      { title: 'Đóng lượt giữ chỗ', tone: 'danger', confirmText: 'Đóng lượt' },
    );
    if (!confirmed || this.actionBusy()) return;
    this.actionBusy.set(true);
    this.operatorService.closeWithoutService(res.id, location.id).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false)),
    ).subscribe({
      next: () => {},
      error: (err: unknown) => void this.dialog.alert(extractApiError(err, 'Không thể đóng lượt giữ chỗ.'), {
        title: 'Không thể thực hiện',
        tone: 'error',
      }),
    });
  }

  // --- Bay Equipment Management ---
  changeBayState(bay: OperatorChargingBay, newState: ChargerEquipmentState): void {
    if (newState === ChargerEquipmentState.Faulted) {
      this.openFaultForm('bay', bay.id, bay.code);
      return;
    }
    if (bay.equipmentState === newState || this.actionBusy()) return;
    this.actionBusy.set(true);
    this.operatorService.updateBayEquipmentState(bay.id, newState).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false)),
    ).subscribe({
      next: () => {},
      error: (err: unknown) => void this.dialog.alert(extractApiError(err, 'Không thể cập nhật trạng thái trụ sạc.'), {
        title: 'Không thể thực hiện',
        tone: 'error',
      }),
    });
  }

  // --- Battery State Management ---
  changeBatteryState(battery: OperatorBattery, nextState: BatteryLifecycleState): void {
    if (nextState === BatteryLifecycleState.Faulted) {
      this.openFaultForm('battery', battery.id, battery.serial);
      return;
    }
    if (battery.state === nextState || this.updatingBatteryId() === battery.id) return;
    this.updatingBatteryId.set(battery.id);
    this.operatorService.updateBatteryState(battery.id, nextState).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.updatingBatteryId.set(null)),
    ).subscribe({
      next: () => {},
      error: (err: unknown) => {
        void this.dialog.alert(extractApiError(err, 'Không thể cập nhật trạng thái viên pin.'), {
          title: 'Không thể thực hiện',
          tone: 'error',
        });
      },
    });
  }

  // Helper labels & styles
  getBayEquipmentLabel(state: ChargerEquipmentState): string {
    switch (state) {
      case ChargerEquipmentState.Active:
        return 'Hoạt động';
      case ChargerEquipmentState.Faulted:
        return 'Báo lỗi / Hỏng';
      case ChargerEquipmentState.Maintenance:
        return 'Bảo trì';
      default:
        return 'Không rõ';
    }
  }

  getParkingLabel(state: ParkingOccupancyState): string {
    return state === ParkingOccupancyState.Occupied ? 'Có xe đỗ' : 'Đang trống';
  }

  getBatteryStateLabel(state: BatteryLifecycleState): string {
    switch (state) {
      case BatteryLifecycleState.Ready:
        return 'Sẵn sàng';
      case BatteryLifecycleState.WithCustomer:
        return 'Khách đang giữ';
      case BatteryLifecycleState.Inspection:
        return 'Chờ kiểm tra';
      case BatteryLifecycleState.Charging:
        return 'Đang sạc';
      case BatteryLifecycleState.Faulted:
        return 'Pin lỗi / Hỏng';
      default:
        return 'Không rõ';
    }
  }

  findBayCode(bayId?: string | null): string {
    if (!bayId) return '—';
    const b = this.stationData()?.bays.find((x) => x.id === bayId);
    return b ? `${b.code} (${b.powerKw} kW)` : bayId.slice(0, 8);
  }

  findBatterySerial(batteryId?: string | null): string {
    if (!batteryId) return '—';
    const b = this.stationData()?.batteries.find((x) => x.id === batteryId);
    return b ? b.serial : batteryId.slice(0, 8);
  }

}
