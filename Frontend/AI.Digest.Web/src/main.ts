import { bootstrapApplication } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import * as echarts from 'echarts';
import { provideEchartsCore } from 'ngx-echarts';
import { AppComponent } from './app/app.component';

bootstrapApplication(AppComponent, {
  providers: [provideHttpClient(), provideEchartsCore({ echarts })]
});
