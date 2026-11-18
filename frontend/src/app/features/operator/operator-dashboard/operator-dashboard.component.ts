import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  BatteryLifecycleState,
  ChargerEquipmentState,
  OperatorActiveReservation,
  OperatorBattery,
  OperatorChargingBay,
  ParkingOccupancyState,
} from '../models/operator.models';
import { OperatorService } from '../services/operator.service';
import { AuthService } from '../../auth/services/auth.service';

type DashboardTab = 'operations' | 'bays' | 'batteries' | 'hours';

@Component({
  selector: 'app-operator-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './operator-dashboard.component.html',
  styleUrls: ['./operator-dashboard.component.scss'],
})
export class OperatorDashboardComponent implements OnInit {
  private readonly operatorService = inject(OperatorService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly currentTab = signal<DashboardTab>('operations');
  readonly stationData = this.operatorService.stationData;
  readonly loading = this.operatorService.loading;
  readonly error = this.operatorService.error;
  readonly currentUser = this.authService.currentUser;

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
  readonly meterError = signal<string | null>(null);

  // Battery filter
  readonly batteryFilter = signal<number | 'all'>('all');

  // Computed summary metrics
  readonly totalBays = computed(() => this.stationData()?.bays.length ?? 0);
  readonly availableBays = computed(
    () =>
      this.stationData()?.bays.filter(
        (b) => b.equipmentState === ChargerEquipmentState.Active && b.parkingState === ParkingOccupancyState.Vacant
      ).length ?? 0
  );
  readonly occupiedBays = computed(
    () => this.stationData()?.bays.filter((b) => b.parkingState === ParkingOccupancyState.Occupied).length ?? 0
  );
  readonly faultedBays = computed(
    () => this.stationData()?.bays.filter((b) => b.equipmentState === ChargerEquipmentState.Faulted).length ?? 0
  );

  readonly totalBatteries = computed(() => this.stationData()?.batteries.length ?? 0);
  readonly readyBatteries = computed(
    () => this.stationData()?.batteries.filter((b) => b.state === BatteryLifecycleState.Ready).length ?? 0
  );
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

  readonly daysOfWeek = [
    { day: 1, label: 'Thứ Hai' },
    { day: 2, label: 'Thứ Ba' },
    { day: 3, label: 'Thứ Tư' },
    { day: 4, label: 'Thứ Năm' },
    { day: 5, label: 'Thứ Sáu' },
    { day: 6, label: 'Thứ Bảy' },
    { day: 0, label: 'Chủ Nhật' },
  ];

  ngOnInit(): void {
    if (!this.authService.isOperator()) {
      this.router.navigate(['/']);
      return;
    }
    this.refresh();
  }

  refresh(): void {
    this.operatorService.getStationDashboard().subscribe({
      error: () => {},
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
      .subscribe({
        next: (res) => {
          this.checkinLoading.set(false);
          this.checkinCode.set('');
          this.checkinSuccess.set(`Check-in thành công cho khách hàng! Mã: ${res.code ? res.code.slice(0, 8) : ''}...`);
          this.currentTab.set('operations');
        },
        error: (err) => {
          this.checkinLoading.set(false);
          if (err.status === 404) {
            this.checkinError.set('Không tìm thấy lượt đặt hoặc mã check-in không đúng.');
          } else if (err.status === 409) {
            this.checkinError.set(err.error?.detail || err.error?.message || 'Mã check-in không thuộc trạm này hoặc đã hết hạn.');
          } else {
            this.checkinError.set('Lỗi xử lý check-in. Vui lòng kiểm tra lại.');
          }
        },
      });
  }

  // --- Charging Session Stepper Logic ---
  confirmVehicleArrived(res: OperatorActiveReservation): void {
    this.operatorService.confirmVehicleArrived(res.id).subscribe({
      next: () => {},
      error: (err) => alert(err.error?.detail || 'Không thể xác nhận xe vào bãi.'),
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
    this.meterKwhInput.set(res.session?.startMeterKwh ? Number(res.session.startMeterKwh) : null);
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
    if (!res || meter === null || isNaN(meter) || meter < 0) {
      this.meterError.set('Vui lòng nhập chỉ số công tơ điện hợp lệ (>= 0).');
      return;
    }

    if (this.meterModalMode() === 'complete') {
      const start = res.session?.startMeterKwh ? Number(res.session.startMeterKwh) : 0;
      if (meter < start) {
        this.meterError.set(`Chỉ số cuối (${meter} kWh) không được nhỏ hơn chỉ số đầu (${start} kWh).`);
        return;
      }
    }

    this.meterSubmitting.set(true);
    this.meterError.set(null);

    if (this.meterModalMode() === 'start') {
      this.operatorService.startCharging(res.id, meter).subscribe({
        next: () => {
          this.meterSubmitting.set(false);
          this.closeMeterModal();
        },
        error: (err: any) => {
          this.meterSubmitting.set(false);
          this.meterError.set(err.error?.detail || err.error?.message || 'Lỗi khi cập nhật chỉ số công tơ.');
        },
      });
    } else {
      this.operatorService.completeCharging(res.id, meter).subscribe({
        next: () => {
          this.meterSubmitting.set(false);
          this.closeMeterModal();
        },
        error: (err: any) => {
          this.meterSubmitting.set(false);
          this.meterError.set(err.error?.detail || err.error?.message || 'Lỗi khi cập nhật chỉ số công tơ.');
        },
      });
    }
  }

  confirmNoCharge(res: OperatorActiveReservation): void {
    if (confirm('Xác nhận khách đã vào chỗ đỗ nhưng quyết định không sạc?')) {
      this.operatorService.confirmNoCharge(res.id).subscribe({
        next: () => {},
        error: (err) => alert(err.error?.detail || 'Không thể xác nhận không sạc.'),
      });
    }
  }

  confirmVehicleDeparted(res: OperatorActiveReservation): void {
    if (confirm('Xác nhận xe đã rời chỗ đỗ? Thao tác này sẽ giải phóng trụ sạc về trạng thái Trống.')) {
      this.operatorService.confirmVehicleDeparted(res.id).subscribe({
        next: () => {},
        error: (err) => alert(err.error?.detail || 'Không thể xác nhận xe rời bãi.'),
      });
    }
  }

  closeWithoutService(res: OperatorActiveReservation): void {
    const location = this.stationData()?.location;
    if (!location) return;
    if (confirm('Đóng lượt giữ chỗ này do khách không sử dụng dịch vụ?')) {
      this.operatorService.closeWithoutService(res.id, location.id).subscribe({
        next: () => {},
        error: (err) => alert(err.error?.detail || 'Không thể đóng lượt giữ chỗ.'),
      });
    }
  }

  // --- Bay Equipment Management ---
  changeBayState(bay: OperatorChargingBay, newState: ChargerEquipmentState): void {
    if (bay.equipmentState === newState) return;
    this.operatorService.updateBayEquipmentState(bay.id, newState).subscribe({
      next: () => {},
      error: (err) => alert(err.error?.detail || 'Không thể cập nhật trạng thái trụ sạc.'),
    });
  }

  // --- Battery State Management ---
  changeBatteryState(battery: OperatorBattery, nextState: BatteryLifecycleState): void {
    this.operatorService.updateBatteryState(battery.id, nextState).subscribe({
      next: () => {},
      error: (err) => alert(err.error?.detail || 'Không thể cập nhật trạng thái viên pin.'),
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

  formatMinutes(min: number): string {
    const h = Math.floor(min / 60);
    const m = min % 60;
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}`;
  }

  getHoursForDay(serviceType: number, day: number): string {
    const list = (this.stationData()?.hours || []).filter(
      (h) => h.serviceType === serviceType && h.day === day
    );
    if (list.length === 0) return 'Nghỉ';
    return list.map((h) => `${this.formatMinutes(h.opensAtMinute)} – ${this.formatMinutes(h.closesAtMinute)}`).join(', ');
  }
}
