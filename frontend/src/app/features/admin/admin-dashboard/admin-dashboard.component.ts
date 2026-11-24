import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { DialogService } from '../../../core/services/dialog.service';
import { extractApiError } from '../../../core/utils/api-error.util';
import { AuthService } from '../../auth/services/auth.service';
import { InvoicesComponent } from '../../invoices/invoices.component';
import {
  AdminLocationItem,
  AdminOperatorSummary,
  AdminReportsSummary,
  OperatorAssignmentRecord,
} from '../models/admin.models';
import { AdminService } from '../services/admin.service';
import { StationOnboardingWizardComponent } from '../station-onboarding-wizard/station-onboarding-wizard.component';

export type AdminTab = 'reports' | 'stations' | 'operators' | 'invoices';
export type ReportRange = '7d' | '30d' | 'month' | 'all';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    DatePipe,
    DecimalPipe,
    FormsModule,
    RouterLink,
    InvoicesComponent,
    StationOnboardingWizardComponent,
  ],
  templateUrl: './admin-dashboard.component.html',
  styleUrl: './admin-dashboard.component.scss',
})
export class AdminDashboardComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly authService = inject(AuthService);
  private readonly dialog = inject(DialogService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly hostEl = inject(ElementRef<HTMLElement>);

  private readonly wizard = viewChild.required(StationOnboardingWizardComponent);
  private readonly createOpDialog = viewChild.required<ElementRef<HTMLDialogElement>>('createOpDialog');
  private readonly historyDialog = viewChild.required<ElementRef<HTMLDialogElement>>('historyDialog');

  readonly currentUser = this.authService.currentUser;
  readonly activeTab = signal<AdminTab>('reports');
  readonly userMenuOpen = signal(false);

  // Loading & feedback
  readonly loading = signal(false);
  readonly actionBusy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);

  // Data
  readonly locations = signal<AdminLocationItem[]>([]);
  readonly operators = signal<AdminOperatorSummary[]>([]);
  readonly report = signal<AdminReportsSummary | null>(null);

  // Report filters
  readonly reportLocationId = signal<string>('');
  readonly reportRange = signal<ReportRange>('30d');

  // Station filters
  readonly stationSearch = signal('');
  readonly stationVisibilityFilter = signal<'all' | 'active' | 'draft'>('all');

  // Operator search & inline assignment drafts
  readonly operatorSearch = signal('');
  readonly transferTargets = signal<Record<string, string>>({});

  // Create Operator Modal fields
  readonly newOpFullName = signal('');
  readonly newOpEmail = signal('');
  readonly newOpPhone = signal('');
  readonly newOpPassword = signal('');
  readonly newOpError = signal<string | null>(null);
  readonly showNewOpPassword = signal(false);

  // Assignment history modal
  readonly historyOperator = signal<AdminOperatorSummary | null>(null);
  readonly historyRecords = signal<OperatorAssignmentRecord[]>([]);
  readonly historyLoading = signal(false);

  readonly locationNameMap = computed(() => {
    const map = new Map<string, string>();
    for (const item of this.locations()) {
      map.set(item.location.id, item.location.name);
    }
    return map;
  });

  readonly filteredLocations = computed(() => {
    const q = this.stationSearch().trim().toLowerCase();
    const vis = this.stationVisibilityFilter();
    return this.locations().filter((item) => {
      if (vis === 'active' && !item.isActive) return false;
      if (vis === 'draft' && item.isActive) return false;
      if (!q) return true;
      return (
        item.location.name.toLowerCase().includes(q) ||
        item.location.address.toLowerCase().includes(q)
      );
    });
  });

  readonly filteredOperators = computed(() => {
    const q = this.operatorSearch().trim().toLowerCase();
    if (!q) return this.operators();
    return this.operators().filter(
      (op) =>
        op.fullName.toLowerCase().includes(q) ||
        op.email.toLowerCase().includes(q) ||
        (op.phoneNumber ?? '').toLowerCase().includes(q) ||
        (op.assignedLocationName ?? '').toLowerCase().includes(q),
    );
  });

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.userMenuOpen()) return;
    const target = event.target as Node | null;
    if (target && !this.hostEl.nativeElement.querySelector('.account-menu')?.contains(target)) {
      this.userMenuOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.userMenuOpen()) {
      this.userMenuOpen.set(false);
    }
  }

  ngOnInit(): void {
    if (!this.authService.isAdmin()) {
      this.router.navigateByUrl(this.authService.home().path);
      return;
    }
    this.refreshAll();
  }

  setTab(tab: AdminTab): void {
    this.activeTab.set(tab);
    this.error.set(null);
    this.notice.set(null);
  }

  toggleUserMenu(): void {
    this.userMenuOpen.update((v) => !v);
  }

  closeUserMenu(): void {
    this.userMenuOpen.set(false);
  }

  shortDisplayName(fullName?: string): string {
    if (!fullName?.trim()) return 'Quản trị';
    const parts = fullName.trim().split(/\s+/);
    return parts.length > 2 ? parts.slice(-2).join(' ') : fullName.trim();
  }

  refreshAll(): void {
    this.loading.set(true);
    this.error.set(null);
    this.loadLocations();
    this.loadOperators();
    this.loadReport();
  }

  loadLocations(): void {
    this.adminService
      .listLocations(1, 100)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => this.locations.set(res.items),
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không tải được danh sách trạm.')),
      });
  }

  loadOperators(): void {
    this.adminService
      .listOperators()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (ops) => {
          this.operators.set(ops);
          const drafts: Record<string, string> = {};
          for (const op of ops) {
            drafts[op.id] = op.assignedLocationId ?? '';
          }
          this.transferTargets.set(drafts);
        },
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không tải được danh sách nhân viên.')),
      });
  }

  loadReport(): void {
    const range = this.reportRange();
    const now = new Date();
    let fromUtc: string | null = null;
    if (range === '7d') {
      fromUtc = new Date(now.getTime() - 7 * 86_400_000).toISOString();
    } else if (range === '30d') {
      fromUtc = new Date(now.getTime() - 30 * 86_400_000).toISOString();
    } else if (range === 'month') {
      fromUtc = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString();
    }

    this.loading.set(true);
    this.adminService
      .getReportsSummary({
        locationId: this.reportLocationId() || null,
        fromUtc,
        toUtc: range === 'all' ? null : now.toISOString(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => this.report.set(data),
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không tải được báo cáo tổng hợp.')),
      });
  }

  setReportRange(range: ReportRange): void {
    this.reportRange.set(range);
    this.loadReport();
  }

  setReportLocation(locationId: string): void {
    this.reportLocationId.set(locationId);
    this.loadReport();
  }

  // Station Onboarding Wizard triggers
  openNewStationWizard(): void {
    this.wizard().openNew();
  }

  openStationConfigWizard(item: AdminLocationItem): void {
    this.wizard().openExistingDraft(item.location.id, item.isActive ? 2 : 4);
  }

  onWizardCompleted(): void {
    this.notice.set('Đã cập nhật dữ liệu trạm thành công.');
    this.refreshAll();
  }

  async toggleVisibility(item: AdminLocationItem): Promise<void> {
    if (this.actionBusy()) return;
    const nextActive = !item.isActive;
    const ok = await this.dialog.confirm(
      nextActive
        ? `Hiển thị công khai trạm "${item.location.name}" lên bản đồ khách hàng?`
        : `Ẩn trạm "${item.location.name}" khỏi bản đồ khách hàng? Các phiên đang vận hành không bị ảnh hưởng.`,
      {
        title: nextActive ? 'Công bố trạm' : 'Ẩn trạm khỏi bản đồ',
        tone: nextActive ? 'primary' : 'warning',
        confirmText: nextActive ? 'Hiển thị trạm' : 'Ẩn trạm',
      },
    );
    if (!ok) return;

    this.actionBusy.set(true);
    this.error.set(null);
    this.adminService
      .setVisibility(item.location.id, nextActive)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.actionBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.notice.set(
            nextActive
              ? `Đã công bố trạm "${item.location.name}" lên bản đồ.`
              : `Đã ẩn trạm "${item.location.name}" khỏi bản đồ công khai.`,
          );
          this.refreshAll();
        },
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không thể thay đổi trạng thái hiển thị trạm.')),
      });
  }

  async rebuildSearchIndex(): Promise<void> {
    if (this.actionBusy()) return;
    const ok = await this.dialog.confirm(
      'Xây dựng lại toàn bộ chỉ mục tìm kiếm Elasticsearch cho mạng lưới trạm?',
      { title: 'Đồng bộ chỉ mục tìm kiếm', tone: 'primary', confirmText: 'Đồng bộ ngay' },
    );
    if (!ok) return;

    this.actionBusy.set(true);
    this.error.set(null);
    this.adminService
      .rebuildSearchIndex()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.actionBusy.set(false)),
      )
      .subscribe({
        next: () => this.notice.set('Đã đồng bộ lại chỉ mục tìm kiếm trạm thành công.'),
        error: (err: unknown) =>
          this.error.set(extractApiError(err, 'Chỉ mục Elasticsearch hiện chưa bật hoặc tạm thời không khả dụng.')),
      });
  }

  // Operator Management
  openCreateOperatorModal(): void {
    this.newOpFullName.set('');
    this.newOpEmail.set('');
    this.newOpPhone.set('');
    this.newOpPassword.set('');
    this.newOpError.set(null);
    this.showNewOpPassword.set(false);
    this.createOpDialog().nativeElement.showModal();
  }

  closeCreateOperatorModal(): void {
    const dlg = this.createOpDialog().nativeElement;
    if (dlg.open) dlg.close();
  }

  submitCreateOperator(): void {
    if (this.actionBusy()) return;
    const fullName = this.newOpFullName().trim();
    const email = this.newOpEmail().trim();
    const password = this.newOpPassword();
    if (fullName.length < 2 || !email || password.length < 8) {
      this.newOpError.set('Vui lòng nhập đầy đủ họ tên, email hợp lệ và mật khẩu tối thiểu 8 ký tự.');
      return;
    }

    this.actionBusy.set(true);
    this.newOpError.set(null);
    this.adminService
      .createOperator({
        fullName,
        email,
        phoneNumber: this.newOpPhone().trim() || null,
        password,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.actionBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.closeCreateOperatorModal();
          this.notice.set(`Đã tạo tài khoản nhân viên "${fullName}" thành công.`);
          this.loadOperators();
        },
        error: (err: unknown) => {
          this.newOpError.set(extractApiError(err, 'Không thể tạo tài khoản nhân viên mới.'));
        },
      });
  }

  setTransferTarget(operatorId: string, locationId: string): void {
    this.transferTargets.update((curr) => ({ ...curr, [operatorId]: locationId }));
  }

  assignOperatorToSelected(op: AdminOperatorSummary): void {
    const targetLocId = this.transferTargets()[op.id];
    if (!targetLocId || targetLocId === op.assignedLocationId || this.actionBusy()) return;

    this.actionBusy.set(true);
    this.error.set(null);
    this.adminService
      .assignOperator(op.id, targetLocId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.actionBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.notice.set(`Đã điều chuyển "${op.fullName}" về trạm mới.`);
          this.loadOperators();
        },
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không thể phân công nhân viên.')),
      });
  }

  async revokeOperatorAssignment(op: AdminOperatorSummary): Promise<void> {
    if (!op.assignedLocationId || this.actionBusy()) return;
    const ok = await this.dialog.confirm(
      `Thu hồi phân công trạm "${op.assignedLocationName}" của nhân viên "${op.fullName}"?`,
      { title: 'Thu hồi phân công trạm', tone: 'warning', confirmText: 'Thu hồi' },
    );
    if (!ok) return;

    this.actionBusy.set(true);
    this.error.set(null);
    this.adminService
      .endOperatorAssignment(op.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.actionBusy.set(false)),
      )
      .subscribe({
        next: () => {
          this.notice.set(`Đã thu hồi phân công của "${op.fullName}".`);
          this.loadOperators();
        },
        error: (err: unknown) => this.error.set(extractApiError(err, 'Không thể thu hồi phân công.')),
      });
  }

  openAssignmentHistory(op: AdminOperatorSummary): void {
    this.historyOperator.set(op);
    this.historyRecords.set([]);
    this.historyLoading.set(true);
    this.historyDialog().nativeElement.showModal();

    this.adminService
      .getOperatorAssignmentHistory(op.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.historyLoading.set(false)),
      )
      .subscribe({
        next: (records) => this.historyRecords.set(records),
        error: () => this.historyRecords.set([]),
      });
  }

  closeHistoryDialog(): void {
    const dlg = this.historyDialog().nativeElement;
    if (dlg.open) dlg.close();
  }

  operatingStatusLabel(status?: string | null): string {
    switch (status) {
      case 'Open':
        return 'Đang mở cửa';
      case 'Maintenance':
        return 'Bảo trì';
      case 'Closed':
        return 'Tạm đóng';
      default:
        return 'Chưa xác nhận';
    }
  }

  async logout(): Promise<void> {
    this.userMenuOpen.set(false);
    const ok = await this.dialog.confirm('Bạn có chắc chắn muốn đăng xuất khỏi Cổng Quản trị không?', {
      title: 'Đăng xuất tài khoản',
      tone: 'warning',
      confirmText: 'Đăng xuất',
      cancelText: 'Ở lại',
    });
    if (!ok) return;
    this.authService.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      complete: () => this.router.navigateByUrl('/login'),
    });
  }
}
