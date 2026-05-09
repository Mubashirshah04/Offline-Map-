import { Component, OnInit, AfterViewInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import maplibregl from 'maplibre-gl';
import { Protocol } from 'pmtiles';

@Component({
  selector: 'app-map',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './map.component.html',
  styleUrls: ['./map.component.css']
})
export class MapComponent implements OnInit, AfterViewInit, OnDestroy {
  private readonly baseUrl = 'http://localhost:5000';

  private map: maplibregl.Map | undefined;
  private currentLocationMarker: maplibregl.Marker | undefined;

  public currentLat: string = '30.1798';
  public currentLng: string = '66.9750';
  public currentZoom: number = 14;
  public isOnline: boolean = true;
  public isSidebarOpen: boolean = true;
  public isStorageOpen: boolean = false;
  public searchQuery: string = '';
  public searchSuggestions: any[] = [];
  public currentLayerName: string = 'google-street';
  public downloadedRegions: any[] = [];
  public isCaching: boolean = false;
  public isAutoDiscover: boolean = false;
  public isHarvesting: boolean = false;
  public selectedCountry: string = 'Pakistan';
  public countrySearchQuery: string = 'Pakistan';
  public isCountryListVisible: boolean = false;
  public filteredCountries: any[] = [];
  
  private statsInterval: any;
  private searchSubject = new Subject<string>();
  private searchSub?: Subscription;

  private allCountries = [
    { name: 'Pakistan', flag: '🇵🇰' }, { name: 'Iran', flag: '🇮🇷' }, { name: 'Afghanistan', flag: '🇦🇫' },
    { name: 'India', flag: '🇮🇳' }, { name: 'China', flag: '🇨🇳' }, { name: 'USA', flag: '🇺🇸' },
    { name: 'Russia', flag: '🇷🇺' }, { name: 'Turkey', flag: '🇹🇷' }, { name: 'Saudi Arabia', flag: '🇸🇦' },
    { name: 'UAE', flag: '🇦🇪' }, { name: 'United Kingdom', flag: '🇬🇧' }, { name: 'Germany', flag: '🇩🇪' },
    { name: 'France', flag: '🇫🇷' }, { name: 'Brazil', flag: '🇧🇷' }, { name: 'Japan', flag: '🇯🇵' },
    { name: 'Canada', flag: '🇨🇦' }, { name: 'Australia', flag: '🇦🇺' }, { name: 'Italy', flag: '🇮🇹' },
    { name: 'South Korea', flag: '🇰🇷' }, { name: 'Indonesia', flag: '🇮🇩' }, { name: 'Mexico', flag: '🇲🇽' },
    { name: 'Egypt', flag: '🇪🇬' }, { name: 'South Africa', flag: '🇿🇦' }, { name: 'Nigeria', flag: '🇳🇬' },
    { name: 'Bangladesh', flag: '🇧🇩' }, { name: 'Vietnam', flag: '🇻🇳' }, { name: 'Thailand', flag: '🇹🇭' },
    { name: 'Iraq', flag: '🇮🇶' }, { name: 'Spain', flag: '🇪🇸' }, { name: 'Poland', flag: '🇵🇱' },
    { name: 'Malaysia', flag: '🇲🇾' }, { name: 'Uzbekistan', flag: '🇺🇿' }, { name: 'Morocco', flag: '🇲🇦' },
    { name: 'Nepal', flag: '🇳🇵' }, { name: 'Peru', flag: '🇵🇪' }, { name: 'Sri Lanka', flag: '🇱🇰' },
    { name: 'Kazakhstan', flag: '🇰🇿' }, { name: 'Netherlands', flag: '🇳🇱' }, { name: 'Belgium', flag: '🇧🇪' },
    { name: 'Greece', flag: '🇬🇷' }, { name: 'Portugal', flag: '🇵🇹' }, { name: 'Sweden', flag: '🇸🇪' },
    { name: 'Norway', flag: '🇳🇴' }, { name: 'Denmark', flag: '🇩🇰' }, { name: 'Finland', flag: '🇫🇮' },
    { name: 'Switzerland', flag: '🇨🇭' }, { name: 'Austria', flag: '🇦🇹' }, { name: 'Israel', flag: '🇮🇱' },
    { name: 'Singapore', flag: '🇸🇬' }, { name: 'New Zealand', flag: '🇳🇿' }, { name: 'Ireland', flag: '🇮🇪' },
    { name: 'Qatar', flag: '🇶🇦' }, { name: 'Kuwait', flag: '🇰🇼' }, { name: 'Oman', flag: '🇴🇲' },
    { name: 'Jordan', flag: '🇯🇴' }, { name: 'Lebanon', flag: '🇱🇧' }, { name: 'Syria', flag: '🇸🇾' },
    { name: 'Azerbaijan', flag: '🇦🇿' }, { name: 'Armenia', flag: '🇦🇲' }, { name: 'Georgia', flag: '🇬🇪' },
    { name: 'Ukraine', flag: '🇺🇦' }, { name: 'Romania', flag: '🇷🇴' }, { name: 'Hungary', flag: '🇭🇺' },
    { name: 'Czech Republic', flag: '🇨🇿' }, { name: 'Slovakia', flag: '🇸🇰' }, { name: 'Bulgaria', flag: '🇧🇬' },
    { name: 'Serbia', flag: '🇷🇸' }, { name: 'Croatia', flag: '🇭🇷' }, { name: 'Slovenia', flag: '🇸🇮' },
    { name: 'Lithuania', flag: '🇱🇹' }, { name: 'Latvia', flag: '🇱🇻' }, { name: 'Estonia', flag: '🇪🇪' },
    { name: 'Argentina', flag: '🇦🇷' }, { name: 'Chile', flag: '🇨🇱' }, { name: 'Colombia', flag: '🇨🇴' },
    { name: 'Venezuela', flag: '🇻🇪' }, { name: 'Ecuador', flag: '🇪🇨' }, { name: 'Bolivia', flag: '🇧🇴' },
    { name: 'Paraguay', flag: '🇵🇾' }, { name: 'Uruguay', flag: '🇺🇾' }, { name: 'Panama', flag: '🇵🇦' },
    { name: 'Costa Rica', flag: '🇨🇷' }, { name: 'Philippines', flag: '🇵🇭' }, { name: 'Myanmar', flag: '🇲🇲' },
    { name: 'Cambodia', flag: '🇰🇭' }, { name: 'Laos', flag: '🇱🇦' }, { name: 'Mongolia', flag: '🇲🇳' },
    { name: 'Tanzania', flag: '🇹🇿' }, { name: 'Kenya', flag: '🇰🇪' }, { name: 'Uganda', flag: '🇺🇬' },
    { name: 'Ethiopia', flag: '🇪🇹' }, { name: 'Ghana', flag: '🇬🇭' }, { name: 'Ivory Coast', flag: '🇨🇮' },
    { name: 'Senegal', flag: '🇸🇳' }, { name: 'Cameroon', flag: '🇨🇲' }, { name: 'Angola', flag: '🇦🇴' },
    { name: 'Zimbabwe', flag: '🇿🇼' }, { name: 'Zambia', flag: '🇿🇲' }, { name: 'Sudan', flag: '🇸🇩' },
    { name: 'Libya', flag: '🇱🇾' }, { name: 'Tunisia', flag: '🇹🇳' }, { name: 'Algeria', flag: '🇩🇿' }
  ];

  public filterCountries() {
    this.isCountryListVisible = true;
    this.filteredCountries = this.allCountries.filter(c =>
      c.name.toLowerCase().includes(this.countrySearchQuery.toLowerCase())
    );
  }

  public selectCountry(country: any) {
    this.countrySearchQuery = country.name;
    this.selectedCountry = country.name;
    this.isCountryListVisible = false;
  }

  public async initGlobalMission() {
    this.isCountryListVisible = false;
    try {
      const resp = await fetch(`https://nominatim.openstreetmap.org/search?country=${this.countrySearchQuery}&format=json&limit=1`);
      const res = await resp.json();
      if (res && res.length > 0) {
        const n = res[0];
        const b = n.boundingbox.map(Number);
        const finalBbox = [b[2], b[0], b[3], b[1]];
        this.showDownloadModal(`Full ${this.countrySearchQuery}`, false, finalBbox);
      } else {
        alert("Could not find boundaries for this country.");
      }
    } catch (e) {
      alert("Network error while fetching country boundaries.");
    }
  }

  public downloadStatus: any = { active: false, total: 0, completed: 0, city: '', paused: false, totalMb: 0, mb: 0 };
  public customModal = { show: false, title: '', body: '', isProvince: false, city: '', isAlreadyDone: false, isDelete: false, bbox: null as any };
  public downloadStats = { speed: '0 KB/s', eta: 'N/A', totalMb: '0.00' };

  constructor(private zone: NgZone, private cdr: ChangeDetectorRef) { }

  ngOnInit(): void {
    window.addEventListener('online', () => {
      this.isOnline = true;
      this.cdr.detectChanges();
    });
    window.addEventListener('offline', () => {
      this.isOnline = false;
      const savedLoc = localStorage.getItem('omega_last_loc');
      if (savedLoc && this.map) {
        const pos = JSON.parse(savedLoc);
        this.map.setCenter([pos[1], pos[0]]); // [lng, lat]
        this.map.setZoom(16);
      } else {
        this.locateUser();
      }
      this.cdr.detectChanges();
    });
    this.isOnline = navigator.onLine;

    // Load from cache first for instant offline startup
    const cache = localStorage.getItem('omega_regions_cache');
    if (cache) this.downloadedRegions = JSON.parse(cache);

    const activeCache = localStorage.getItem('omega_active_download');
    if (activeCache) this.downloadStatus = JSON.parse(activeCache);

    this.loadDownloadedRegions();
    this.startStatsPolling();

    // 🚀 VISIBILITY API: Save battery & bandwidth when tab is hidden
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        this.startStatsPolling(5000); // Slow down to 5s
      } else {
        this.startStatsPolling(1000); // Back to 1s
      }
    });

    // 🚀 SMART SEARCH DEBOUNCE: Prevents 429 Errors
    this.searchSub = this.searchSubject.pipe(
      debounceTime(500),
      distinctUntilChanged()
    ).subscribe(query => {
      this.performActualSearch(query);
    });
  }

  ngAfterViewInit(): void {
    // Register PMTiles Protocol for Nginx served PMTiles
    let protocol = new Protocol();
    maplibregl.addProtocol('pmtiles', protocol.tile);

    this.initMap();
    if (this.isOnline) {
      setTimeout(() => { this.locateUser(); }, 500);
    }
  }

  ngOnDestroy(): void {
    if (this.map) this.map.remove();
  }

  private initMap(): void {
    const savedLoc = localStorage.getItem('omega_last_loc');
    const savedZoom = localStorage.getItem('omega_last_zoom');

    // Default MapLibre GL needs [lng, lat]
    const startPos: [number, number] = savedLoc ? [JSON.parse(savedLoc)[1], JSON.parse(savedLoc)[0]] : [66.9750, 30.1798];
    const startZoom = savedZoom ? parseInt(savedZoom) : 14;

    this.map = new maplibregl.Map({
      container: 'map-container',
      style: {
        version: 8,
        sources: {},
        layers: []
      },
      center: startPos,
      zoom: startZoom,
      attributionControl: false,
      antialias: true, // 🚀 HIGH QUALITY RENDERING
      maxPitch: 85,
      bearingSnap: 7,
      trackResize: true
    } as any);

    this.map.on('load', () => {
      this.switchLayer(this.currentLayerName);
      if (savedLoc) {
        const pos = JSON.parse(savedLoc);
        this.updateUserMarker([pos[1], pos[0]]);
      }
    });

    this.map.on('move', () => {
      this.zone.run(() => {
        if (this.map) {
          const center = this.map.getCenter();
          this.currentLat = center.lat.toFixed(4);
          this.currentLng = center.lng.toFixed(4);
          localStorage.setItem('omega_last_loc', JSON.stringify([center.lat, center.lng]));
          localStorage.setItem('omega_last_zoom', this.map.getZoom().toString());

          if (this.isAutoDiscover) {
            this.triggerAutoDiscover(true);
          }

          this.cdr.markForCheck();
        }
      });
    });

    this.map.on('zoomend', () => {
      this.zone.run(() => {
        if (this.map) {
          this.currentZoom = this.map.getZoom();
          localStorage.setItem('omega_last_zoom', this.currentZoom.toString());
          this.cdr.markForCheck();
        }
      });
    });

    this.map.on('click', () => {
      this.zone.run(() => {
        if (this.map && this.map.getLayer('highlight-area-fill')) {
          this.map.removeLayer('highlight-area-fill');
          this.map.removeLayer('highlight-area-line');
          this.map.removeSource('highlight-area');
          this.cdr.detectChanges();
        }
      });
    });
  }

  private updateUserMarker(coords: [number, number]): void {
    if (!this.map) return;

    if (!this.currentLocationMarker) {
      const el = document.createElement('div');
      el.className = 'google-blue-dot-container';
      el.innerHTML = `<div class="google-blue-dot"><div class="google-blue-dot-pulse"></div></div>`;

      this.currentLocationMarker = new maplibregl.Marker({ element: el })
        .setLngLat(coords)
        .addTo(this.map);
    } else {
      this.currentLocationMarker.setLngLat(coords);
    }
  }

  public switchLayer(layerName: string, cityOverride?: string): void {
    if (!this.map || !this.map.isStyleLoaded()) return;
    this.currentLayerName = layerName;
    console.log("🚀 Switching Layer to:", layerName);

    // Smart Offline Detection: If offline and no override, pick the first downloaded region
    let targetCity = cityOverride || (this.downloadStatus.active ? this.downloadStatus.city : null);

    if (!targetCity && !this.isOnline && this.downloadedRegions.length > 0) {
      targetCity = this.downloadedRegions[0].city;
    }

    // Default fallback if still nothing
    if (!targetCity) targetCity = 'google-street';

    const localPmtilesUrl = `http://localhost:5000/tiles/${targetCity.replace(/ /g, '_')}.pmtiles`;

    // Remove old layers
    ['base-layer', 'water-layer', 'earth-layer', 'roads-layer', 'buildings-layer'].forEach(l => {
      if (this.map?.getLayer(l)) this.map.removeLayer(l);
    });
    if (this.map?.getSource('base-source')) this.map.removeSource('base-source');

    // 🚀 PURE RASTER ARCHITECTURE: No more PMTiles overhead
    this.addLayerSource(null, !this.isOnline, layerName, targetCity);
  }

  public viewArea(reg: any): void {
    if (!this.map) return;

    if (reg.bbox_json) {
      const bbox = JSON.parse(reg.bbox_json);
      const bounds: [number, number][] = [[bbox[0], bbox[1]], [bbox[2], bbox[3]]];

      if (this.map.getLayer('highlight-area-fill')) {
        this.map.removeLayer('highlight-area-fill');
        this.map.removeLayer('highlight-area-line');
        this.map.removeSource('highlight-area');
      }

      this.map.addSource('highlight-area', {
        type: 'geojson',
        data: {
          type: 'Feature',
          geometry: {
            type: 'Polygon',
            coordinates: [[
              [bbox[0], bbox[1]],
              [bbox[2], bbox[1]],
              [bbox[2], bbox[3]],
              [bbox[0], bbox[3]],
              [bbox[0], bbox[1]]
            ]]
          },
          properties: {}
        }
      });

      this.map.addLayer({
        id: 'highlight-area-fill',
        type: 'fill',
        source: 'highlight-area',
        paint: { 'fill-color': '#1a73e8', 'fill-opacity': 0.15 }
      });

      this.map.addLayer({
        id: 'highlight-area-line',
        type: 'line',
        source: 'highlight-area',
        paint: { 'line-color': '#1a73e8', 'line-width': 2, 'line-dasharray': [5, 5] }
      });

      this.map.fitBounds(bounds as maplibregl.LngLatBoundsLike, { padding: 50, duration: 2000 });
      this.switchLayer(this.currentLayerName, reg.city);
    }
    this.isStorageOpen = false;
  }

  private addLayerSource(pmtilesUrl: string | null, isOffline: boolean, layerName: string, targetCity: string = ''): void {
    if (!this.map) return;
    
    // 🚀 LAYER-SPECIFIC STORAGE: Each layer (Street, Satellite, ArcGIS) gets its own sub-folder
    const folderName = (targetCity ? targetCity.replace(/ /g, '_') : 'Auto-Discovered_Data');
    const folderPath = folderName;

    // 🚀 SMART SCALING: If a layer (like ArcGIS) ends early, we tell the engine to overzoom the last tiles
    const sourceMaxZoom = (layerName.includes('arcgis')) ? 18 : 22;

    if (isOffline) {
      let layerFolder = 'street';
      if (layerName.includes('satellite') || layerName.includes('hybrid')) layerFolder = 'satellite';
      if (layerName.includes('arcgis')) layerFolder = 'arcgis';
      if (layerName.includes('night')) layerFolder = 'night';

      const localUrl = `${this.baseUrl}/tiles/${folderPath}/${layerFolder}/{z}/{x}/{y}.png`;

      this.map.addSource('base-source', {
        type: 'raster',
        tiles: [localUrl],
        tileSize: 256,
        maxzoom: sourceMaxZoom
      });
      this.map.addLayer({
        id: 'base-layer', type: 'raster', source: 'base-source', minzoom: 0, maxzoom: 22
      });
    } else {
      // 🛰️ ONLINE LIVE MODES
      let onlineUrl = `https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}`; // Default Street
      
      if (layerName.includes('satellite') || layerName.includes('hybrid')) {
        onlineUrl = `https://mt1.google.com/vt/lyrs=y&x={x}&y={y}&z={z}`;
      } else if (layerName.includes('arcgis')) {
        onlineUrl = `https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}`;
      } else if (layerName.includes('dark') || layerName.includes('night')) {
        // 🌑 ULTRA-NIGHT STYLE: Simplified and confirmed Google apistyle
        onlineUrl = `https://mt1.google.com/vt/lyrs=m&x={x}&y={y}&z={z}&apistyle=s.t:1|p.v:on,s.t:2|p.v:off,s.t:3|p.v:on|p.c:#ff242f3e,s.t:4|p.v:on|p.c:#ff1f2835`;
      }

      this.map.addSource('base-source', {
        type: 'raster',
        tiles: [onlineUrl],
        tileSize: 256,
        maxzoom: sourceMaxZoom
      });
      this.map.addLayer({
        id: 'base-layer', type: 'raster', source: 'base-source', minzoom: 0, maxzoom: 22
      });
    }
  }

  public toggleSidebar(): void {
    this.isSidebarOpen = !this.isSidebarOpen;
    setTimeout(() => { if (this.map) this.map.resize(); }, 300);
  }

  public async performSearch(): Promise<void> {
    if (!this.searchQuery) return;
    try {
      const osmRes = await fetch(`${this.baseUrl}/api/search?q=${this.searchQuery}&limit=1`);
      const results = await osmRes.json();
      if (results.length > 0 && this.map) {
        this.map.flyTo({ center: [parseFloat(results[0].lon), parseFloat(results[0].lat)], zoom: 16 });
        this.searchSuggestions = [];
      }
    } catch (err) { console.error(err); }
  }

  public onSearchInput(): void {
    if (this.searchQuery.length < 3) { this.searchSuggestions = []; return; }
    this.searchSubject.next(this.searchQuery);
  }

  private performActualSearch(query: string): void {
    fetch(`${this.baseUrl}/api/search?q=${query}&limit=5`)
      .then(res => res.json())
      .then(data => { 
        this.searchSuggestions = data; 
        this.cdr.detectChanges();
      })
      .catch(err => console.error(err));
  }

  public selectSuggestion(s: any): void {
    this.searchQuery = s.display_name;
    this.searchSuggestions = [];
    if (this.map) this.map.flyTo({ center: [parseFloat(s.lon), parseFloat(s.lat)], zoom: 16 });
  }

  public locateUser(): void {
    if (navigator.geolocation && this.map) {
      navigator.geolocation.getCurrentPosition((pos) => {
        const coords: [number, number] = [pos.coords.longitude, pos.coords.latitude];
        this.map?.flyTo({ center: coords, zoom: 16 });
        this.updateUserMarker(coords);
      });
    }
  }

  public loadDownloadedRegions(): void {
    fetch(`${this.baseUrl}/all-downloads?t=${Date.now()}`).then(r => r.json()).then(d => {
      this.downloadedRegions = d;
      localStorage.setItem('omega_regions_cache', JSON.stringify(d));
      this.cdr.detectChanges();
    }).catch(err => {
      console.error("Failed to fetch regions, using cache", err);
    });
  }

  public toggleStorage(): void { this.isStorageOpen = !this.isStorageOpen; }
  public closeModal(): void { this.customModal.show = false; }
  public shareLocation(): void { alert('Location: ' + this.currentLat + ', ' + this.currentLng); }
  public downloadCurrentArea(): void { this.showDownloadModal('Current View', false); }

  public toggleLayer(): void {
    const layers = ['google-street', 'satellite', 'arcgis-street', 'arcgis-dark'];
    const idx = layers.indexOf(this.currentLayerName);
    this.switchLayer(layers[(idx + 1) % layers.length]);
  }

  public stopDownload(): void {
    fetch(`${this.baseUrl}/stop-download`, { method: 'POST' }).then(() => {
      this.downloadStatus = { active: false, total: 0, completed: 0, city: '', paused: false, totalMb: 0, mb: 0 };
      this.isHarvesting = false;
      localStorage.removeItem('omega_active_download');
      this.loadDownloadedRegions();
    });
  }

  public get currentPreviewImage(): string {
    return `https://mt1.google.com/vt/lyrs=m&x=11240&y=6749&z=14`;
  }

  public get isSatellite(): boolean { return this.currentLayerName === 'satellite'; }

  public showDownloadModal(area: string, isAll: boolean, bbox: any = null): void {
    this.customModal = { show: true, title: 'Download', body: `Harvest ${area}?`, isProvince: isAll, city: area, isAlreadyDone: false, isDelete: false, bbox: bbox };
  }
  public showProvinceSelector(): void {
    this.customModal = { show: true, title: 'Global City / Region Selector', body: 'Choose a city or province to harvest:', isProvince: true, city: 'Lahore', isAlreadyDone: false, isDelete: false, bbox: null };
  }
  public searchCityName: string = '';
  public searchCity(): void {
    if (!this.searchCityName) return;
    fetch(`https://nominatim.openstreetmap.org/search?q=${this.searchCityName}&format=json&limit=1`)
      .then(r => r.json())
      .then(data => {
        if (data && data.length > 0) {
          const b = data[0].boundingbox; // [lat1, lat2, lon1, lon2]
          this.customModal.city = data[0].display_name.split(',')[0];
          this.customModal.bbox = [parseFloat(b[2]), parseFloat(b[0]), parseFloat(b[3]), parseFloat(b[1])];
          this.customModal.body = `Found: ${data[0].display_name}. Harvest this area?`;
          this.cdr.detectChanges();
        } else {
          alert('City not found!');
        }
      });
  }

  public startOfflineHarvest(): void {
    if (!this.map) return;
    const center = this.map.getCenter();
    let bbox: any;

    if (this.customModal.bbox) {
      bbox = this.customModal.bbox;
    } else if (this.customModal.city === 'Current View') {
      const bounds = this.map.getBounds();
      bbox = [bounds.getWest(), bounds.getSouth(), bounds.getEast(), bounds.getNorth()];
    } else {
      // 🗺️ GLOBAL CITY & PROVINCE BBOX LOOKUP
      const globalBboxes: { [key: string]: number[] } = {
        'Punjab': [69.2, 27.7, 75.4, 34.0],
        'Sindh': [66.6, 23.6, 71.1, 28.5],
        'KPK': [69.2, 31.0, 74.1, 37.0],
        'Balochistan': [60.8, 24.8, 70.3, 32.1],
        'Full Pakistan': [60.8, 23.6, 79.4, 37.1],
        'Karachi': [66.8, 24.7, 67.3, 25.1],
        'Lahore': [74.2, 31.3, 74.5, 31.7],
        'Dubai': [55.1, 24.9, 55.5, 25.3],
        'London': [-0.3, 51.3, 0.2, 51.7],
        'New York': [-74.1, 40.6, -73.8, 40.9],
        'Tokyo': [139.5, 35.5, 139.9, 35.9],
        'Riyadh': [46.5, 24.5, 46.9, 24.9],
        'Istanbul': [28.8, 40.9, 29.2, 41.2]
      };
      
      bbox = globalBboxes[this.customModal.city] || [center.lng - 0.15, center.lat - 0.15, center.lng + 0.15, center.lat + 0.15];
    }

    fetch(`${this.baseUrl}/start-download`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        city: this.customModal.city,
        bbox: bbox,
        zoomMin: 0,
        zoomMax: 21
      })
    }).then(() => {
      this.closeModal();
      this.downloadStatus.active = true;
      this.downloadStatus.city = this.customModal.city;
      this.startStatsPolling(500); // 🚀 INSTANT FEEDBACK
    });
  }

  public startStatsPolling(interval: number = 500): void {
    if (this.statsInterval) clearInterval(this.statsInterval);
    this.statsInterval = setInterval(() => {
      // Don't poll regions every second, only every 10 seconds to reduce DB load
      if (Date.now() % 10000 < interval) this.loadDownloadedRegions();

      const pollUrl = `${this.baseUrl}/download-status?t=${Date.now()}`;
      fetch(pollUrl, { cache: 'no-store', signal: AbortSignal.timeout(5000) })
        .then(r => r.json())
        .then(status => {
          if (status.active) {
              this.downloadStatus = {
                active: true,
                city: status.city,
                completed: status.completed,
                total: status.total,
                mb: status.mb,
                totalMb: status.totalMb,
                paused: status.paused || false
              };
              localStorage.setItem('omega_active_download', JSON.stringify(this.downloadStatus));
          } else {
            // Check auto-discovery if no explicit task
            const auto = this.downloadedRegions.find(r => r.city.includes('Auto-Discovered'));
            if (auto && (auto.status.includes('Harvesting') || auto.status.includes('Queued'))) {
               this.downloadStatus = {
                 active: true,
                 city: '🛰️ ' + auto.city,
                 completed: auto.completed_tiles,
                 total: auto.total_tiles,
                 mb: auto.size_mb,
                 totalMb: (auto.total_tiles * 0.012).toFixed(2)
               };
            } else {
              localStorage.removeItem('omega_active_download');
              this.downloadStatus.active = false;
            }
          }
          this.cdr.detectChanges();
        })
        .catch(err => {
          console.warn("OMEGA Shield: Polling retrying in next tick...");
        });
    }, 1000);
  }

  public pauseDownload(): void { fetch(`${this.baseUrl}/pause-download`, { method: 'POST' }); }
  public resumeDownload(): void { fetch(`${this.baseUrl}/resume-download`, { method: 'POST' }); }
  public resumeSpecific(city: string): void {
    fetch(`${this.baseUrl}/resume-specific`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ city })
    }).then(() => this.loadDownloadedRegions());
  }
  public showDeleteModal(city: string): void { this.customModal = { show: true, title: 'Delete', body: `Delete ${city}?`, isProvince: false, city, isAlreadyDone: false, isDelete: true, bbox: null }; }
  public confirmStopDownload(): void {
    const city = this.downloadStatus.city.replace('🛰️ ', '');
    this.customModal = {
      show: true,
      title: 'Stop & Delete',
      body: `Aborting mission for ${city}. Delete all progress data?`,
      isProvince: false,
      city,
      isAlreadyDone: false,
      isDelete: true,
      bbox: null
    };
  }

  public confirmDelete(): void {
    if (this.downloadStatus.active && this.downloadStatus.city.includes(this.customModal.city)) {
      this.stopDownload();
    }

    fetch(`${this.baseUrl}/delete-download`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ city: this.customModal.city })
    }).then(() => {
      this.downloadStatus = { active: false, total: 0, completed: 0, city: '', paused: false, totalMb: 0, mb: 0 };
      localStorage.removeItem('omega_active_download');
      this.closeModal();
      this.loadDownloadedRegions();
      this.cdr.detectChanges();
    });
  }

  public confirmDeleteAll(): void {
    if (!confirm("☢️ NUCLEAR RESET: This will permanently delete ALL offline maps and FREE UP Disk Space. Procceed?")) return;

    this.isCaching = true;
    fetch(`${this.baseUrl}/delete-all-data`, { method: 'POST' })
      .then(() => {
        this.isCaching = false;
        this.loadDownloadedRegions();
        alert("✅ DISK PURGED: System is now 100% Clean Slate.");
        this.cdr.detectChanges();
      });
  }

  private lastAutoSave: number = 0;
  private triggerAutoDiscover(isEnabled: boolean): void {
    const now = Date.now();
    if (now - this.lastAutoSave < 3000) return;
    this.lastAutoSave = now;

    if (!this.map) return;
    const bounds = this.map.getBounds();
    const bbox = [bounds.getWest(), bounds.getSouth(), bounds.getEast(), bounds.getNorth()];

    fetch(`${this.baseUrl}/auto-discover`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ bbox, enabled: true })
    }).then(() => this.loadDownloadedRegions());
  }

  public formatBytes(mb: any): string {
    const val = parseFloat(mb);
    if (isNaN(val) || val === 0) return '0 MB';
    
    // Logic: mb -> gb -> tb
    if (val >= 1048576) return (val / 1048576).toFixed(2) + ' TB';
    if (val >= 1024) return (val / 1024).toFixed(2) + ' GB';
    return val.toFixed(2) + ' MB';
  }
}
