import { ChangeDetectionStrategy, Component } from '@angular/core';

interface PlatformMetric {
  value: string;
  unit: string;
  label: string;
  path: string;
}

@Component({
  selector: 'app-brand-showcase',
  templateUrl: './brand-showcase.component.html',
  styleUrl: './brand-showcase.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandShowcaseComponent {
  protected readonly metrics: PlatformMetric[] = [
    {
      value: '500',
      unit: '+',
      label: 'điểm dữ liệu mẫu',
      path: 'M2 25 C16 24, 19 15, 34 18 S52 7, 70 10 S87 3, 98 5',
    },
    {
      value: '23',
      unit: '',
      label: 'khu vực mô phỏng',
      path: 'M2 26 C15 18, 26 25, 38 15 S59 18, 69 8 S86 12, 98 3',
    },
    {
      value: '15',
      unit: 'phút',
      label: 'thời gian giữ chỗ',
      path: 'M2 22 C14 25, 24 19, 36 21 S55 12, 67 15 S85 6, 98 8',
    },
  ];

  protected readonly journeySteps = ['Tìm trạm', 'Giữ chỗ', 'Sạc hoặc đổi pin'];
}
