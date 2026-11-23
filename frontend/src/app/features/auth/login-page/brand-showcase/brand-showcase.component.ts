import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface NetworkMetric {
  value: string;
  unit: string;
  label: string;
  sublabel: string;
}

interface MobilityFeature {
  icon: 'navigation' | 'zap' | 'shield';
  title: string;
  desc: string;
}

@Component({
  selector: 'app-brand-showcase',
  imports: [RouterLink],
  templateUrl: './brand-showcase.component.html',
  styleUrl: './brand-showcase.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandShowcaseComponent {
  protected readonly metrics: NetworkMetric[] = [
    {
      value: '1.250',
      unit: '+',
      label: 'Trụ sạc hoạt động',
      sublabel: 'DC Fast 60 - 360kW',
    },
    {
      value: '99.8',
      unit: '%',
      label: 'Độ khả dụng mạng lưới',
      sublabel: 'Vận hành 24/7',
    },
    {
      value: '63',
      unit: '/63',
      label: 'Tỉnh thành phủ sóng',
      sublabel: 'Toàn quốc',
    },
  ];

  protected readonly features: MobilityFeature[] = [
    {
      icon: 'navigation',
      title: 'Định vị & Chỉ đường',
      desc: 'Tìm trạm sạc gần nhất theo lộ trình thời gian thực.',
    },
    {
      icon: 'zap',
      title: 'Sạc nhanh & Đổi pin',
      desc: 'Sạc siêu tốc DC và trạm đổi pin tự động dưới 3 phút.',
    },
    {
      icon: 'shield',
      title: 'Giữ chỗ tin cậy',
      desc: 'Đặt trước chỗ sạc 15 phút, đảm bảo trụ trống khi đến.',
    },
  ];
}
