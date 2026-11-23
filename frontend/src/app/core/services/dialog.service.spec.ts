import { TestBed } from '@angular/core/testing';
import { DialogService } from './dialog.service';

describe('DialogService', () => {
  let service: DialogService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DialogService);
  });

  it('opens an alert dialog and resolves when closed', async () => {
    const promise = service.alert('Không thể cập nhật trạng thái.');
    expect(service.current()?.kind).toBe('alert');
    expect(service.current()?.message).toBe('Không thể cập nhật trạng thái.');

    service.close(true);
    await expect(promise).resolves.toBeUndefined();
    expect(service.current()).toBeNull();
  });

  it('resolves confirm dialog with true or false', async () => {
    const accepted = service.confirm('Xác nhận xe đã rời bãi?');
    expect(service.current()?.kind).toBe('confirm');
    service.close(true);
    await expect(accepted).resolves.toBe(true);

    const rejected = service.confirm('Hủy lượt giữ chỗ?');
    service.close(false);
    await expect(rejected).resolves.toBe(false);
  });

  it('queues concurrent dialogs sequentially', async () => {
    const first = service.alert('Lỗi 1');
    const second = service.confirm('Xác nhận 2');

    expect(service.current()?.message).toBe('Lỗi 1');
    service.close(true);
    await first;

    expect(service.current()?.message).toBe('Xác nhận 2');
    service.close(true);
    await expect(second).resolves.toBe(true);
    expect(service.current()).toBeNull();
  });
});
