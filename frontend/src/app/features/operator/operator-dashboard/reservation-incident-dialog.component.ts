import { Component, DestroyRef, ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, of, Subject, switchMap } from 'rxjs';
import { IncidentOptions, IncidentResolution, OperatorService } from '../services/operator.service';

@Component({
  selector: 'app-reservation-incident-dialog',
  template: `
    <dialog #panel aria-labelledby="incident-title" (cancel)="lockWhileSaving($event)">
      <header>
        <div>
          <h2 id="incident-title">Xử lý sự cố</h2>
          <p>{{ report()?.resource }}</p>
        </div>
        <button class="close" type="button" aria-label="Đóng" [disabled]="saving()" (click)="panel.close()">×</button>
      </header>
      <div class="body">
        <p class="description">{{ report()?.description }}</p>
        @if (message(); as text) { <p class="error" role="alert">{{ text }}</p> }
        @if (loading()) {
          <p role="status">Đang kiểm tra phương án…</p>
        } @else if (options(); as choices) {
          @if (!choices.canChangeReservation) {
            <p>Lượt này không thể chuyển hoặc hủy qua báo cáo sự cố. Bạn có thể đóng báo cáo.</p>
          } @else if (choices.candidates.length) {
            <label for="incident-replacement">Tài nguyên thay thế</label>
            <select id="incident-replacement" [value]="replacementId()" [disabled]="saving()"
              (change)="replacementId.set($any($event.target).value)">
              <option value="">Chọn tài nguyên</option>
              @for (candidate of choices.candidates; track candidate.id) {
                <option [value]="candidate.id">{{ candidate.code }}</option>
              }
            </select>
            <p class="hint">Mã và hạn giữ chỗ được giữ nguyên. Khách sẽ nhận thông báo.</p>
          } @else {
            <p>Trạm đã hết tài nguyên thay thế. Nếu xác nhận hủy, khách sẽ nhận thông báo và lý do.</p>
          }
        }
      </div>
      <footer>
        <button type="button" [disabled]="saving()" (click)="panel.close()">Quay lại</button>
        @if (options(); as choices) {
          @if (!choices.canChangeReservation) {
            <button type="button" class="primary" [disabled]="saving()" (click)="resolve(resolution.Recorded)">Đóng báo cáo</button>
          } @else if (choices.candidates.length) {
            <button type="button" class="primary" [disabled]="saving() || !replacementId()"
              (click)="resolve(resolution.Reassigned)">Xác nhận chuyển</button>
          } @else {
            <button type="button" class="danger" [disabled]="saving()"
              (click)="resolve(resolution.Cancelled)">Xác nhận hủy lượt</button>
          }
        } @else if (!loading()) {
          <button type="button" class="primary" (click)="reload()">Thử lại</button>
        }
      </footer>
    </dialog>
  `,
  styleUrl: './reservation-incident-dialog.component.scss',
})
export class ReservationIncidentDialogComponent {
  private readonly operator = inject(OperatorService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly panel = viewChild.required<ElementRef<HTMLDialogElement>>('panel');
  private readonly requests = new Subject<string>();
  readonly resolved = output<string>();
  readonly resolution = IncidentResolution;
  readonly report = signal<{ id: string; description: string; resource: string } | null>(null);
  readonly options = signal<IncidentOptions | null>(null);
  readonly replacementId = signal('');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly message = signal<string | null>(null);

  constructor() {
    this.requests.pipe(
      switchMap((id) => this.operator.getIncidentOptions(id).pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe((options) => {
      this.loading.set(false);
      this.options.set(options);
      if (!options) this.message.set('Không tải được phương án. Vui lòng thử lại.');
    });
  }

  open(report: { id: string; description: string }, resource: string): void {
    this.report.set({ ...report, resource });
    this.reload();
    this.panel().nativeElement.showModal();
  }

  reload(message: string | null = null): void {
    const report = this.report();
    if (!report) return;
    this.message.set(message);
    this.options.set(null);
    this.replacementId.set('');
    this.loading.set(true);
    this.requests.next(report.id);
  }

  lockWhileSaving(event: Event): void {
    if (this.saving()) event.preventDefault();
  }

  resolve(resolution: IncidentResolution): void {
    const report = this.report();
    if (!report || this.saving() || !this.options()) return;
    const replacementId = resolution === IncidentResolution.Reassigned ? this.replacementId() : undefined;
    if (resolution === IncidentResolution.Reassigned && !replacementId) return;
    this.saving.set(true);
    this.message.set(null);
    this.operator.resolveIncident(report.id, resolution, replacementId).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        this.panel().nativeElement.close();
        this.resolved.emit(resolution === IncidentResolution.Reassigned ? 'Đã chuyển tài nguyên cho lượt đặt.'
          : resolution === IncidentResolution.Cancelled ? 'Đã hủy lượt đặt do sự cố.' : 'Đã đóng báo cáo sự cố.');
      },
      error: (error) => {
        if (error.status === 409) {
          this.reload('Tình trạng lượt đặt hoặc tài nguyên đã thay đổi. Kiểm tra lại phương án bên dưới.');
        } else {
          this.message.set('Không thể xác nhận xử lý sự cố. Vui lòng thử lại.');
        }
      },
    });
  }
}
