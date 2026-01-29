# PWA缓存清理完整发布指南

## 发布新版本时的缓存清理流程

### 方式一：自动化版本更新(推荐)

#### 1. 使用版本更新脚本

```powershell
# 更新缓存版本号
.\update-cache-version.ps1 -Version "v2"
```

#### 2. 编译发布

```powershell
dotnet publish -c Release
```

#### 3. 部署后验证

打开浏览器控制台,查看日志确认:
```
[ServiceWorker] 删除旧缓存: webcode-pwa-v1
[ServiceWorker] 删除旧缓存: webcode-static-v1
[ServiceWorker] 激活完成
```

---

### 方式二：手动修改版本号

#### 1. 编辑 `service-worker.js` (第6-8行)

**修改前:**
```javascript
const CACHE_NAME = 'webcode-pwa-v1';
const STATIC_CACHE_NAME = 'webcode-static-v1';
const DYNAMIC_CACHE_NAME = 'webcode-dynamic-v1';
```

**修改后:**
```javascript
const CACHE_NAME = 'webcode-pwa-v2';  // v1 → v2
const STATIC_CACHE_NAME = 'webcode-static-v2';
const DYNAMIC_CACHE_NAME = 'webcode-dynamic-v2';
```

#### 2. 编译发布

```powershell
dotnet publish -c Release -o ./publish
```

---

## 缓存清理原理

### 自动清理机制 (已实现)

Service Worker的 `activate` 事件会自动处理:

```javascript
// service-worker.js 第68-88行
self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames
          .filter((cacheName) => {
            // 删除所有不是当前版本的旧缓存
            return cacheName !== STATIC_CACHE_NAME && 
                   cacheName !== DYNAMIC_CACHE_NAME &&
                   cacheName.startsWith('webcode-');
          })
          .map((cacheName) => caches.delete(cacheName))
      );
    })
  );
});
```

### 用户端更新流程

1. **用户访问网站** → Service Worker检测到新版本
2. **后台安装新SW** → 下载并缓存新资源
3. **等待激活** → 用户关闭所有标签页后激活
4. **自动清理** → 删除旧版本缓存
5. **重新加载** → 用户下次访问使用新缓存

---

## 版本命名建议

### 语义化版本号

```javascript
// 主要更新
webcode-pwa-v2.0.0

// 功能更新
webcode-pwa-v1.1.0

// Bug修复
webcode-pwa-v1.0.1

// 按日期
webcode-pwa-20260129
```

### 统一更新所有缓存名

确保三个缓存名版本号保持一致:
```javascript
const CACHE_NAME = 'webcode-pwa-v2';
const STATIC_CACHE_NAME = 'webcode-static-v2';  // 同步
const DYNAMIC_CACHE_NAME = 'webcode-dynamic-v2'; // 同步
```

---

## 强制用户更新(可选)

### 方案A: 自动刷新

修改 `pwa-registration.js`:

```javascript
navigator.serviceWorker.addEventListener('controllerchange', () => {
  console.log('[PWA] 新版本已激活,自动刷新...');
  window.location.reload();  // 自动刷新
});
```

### 方案B: 提示用户更新

```javascript
onUpdateReady: function(worker) {
  // 显示更新提示
  if (confirm('发现新版本,是否立即更新?')) {
    worker.postMessage({ action: 'skipWaiting' });
    window.location.reload();
  }
}
```

---

## 完整发布检查清单

- [ ] 1. 更新 service-worker.js 中的缓存版本号
- [ ] 2. 更新 STATIC_ASSETS 数组(如有新增文件)
- [ ] 3. 运行 `dotnet publish -c Release`
- [ ] 4. 部署到服务器
- [ ] 5. 清除CDN缓存(如果使用)
- [ ] 6. 在浏览器中测试:
  - [ ] 打开 DevTools → Application → Service Workers
  - [ ] 点击 "Update" 按钮
  - [ ] 查看 Cache Storage,确认旧缓存已删除
- [ ] 7. 在移动设备上测试PWA更新

---

## 故障排查

### 问题1: 用户看不到新版本

**原因**: Service Worker未更新

**解决**:
```javascript
// 手动触发更新检查
navigator.serviceWorker.getRegistration().then(reg => {
  reg.update();
});
```

### 问题2: 旧缓存没有删除

**检查**:
1. 确认版本号已更新
2. Chrome DevTools → Application → Cache Storage
3. 手动清除: `Clear storage` → `Clear site data`

### 问题3: iOS Safari缓存顽固

**解决**: iOS用户需要:
1. 设置 → Safari → 清除历史记录与网站数据
2. 或删除主屏幕图标重新添加

---

## 开发环境测试

### 跳过等待立即激活(仅开发用)

```javascript
// service-worker.js install事件中
self.skipWaiting();  // ✓ 已添加

// activate事件中
self.clients.claim();  // ✓ 已添加
```

### Chrome DevTools调试

1. F12 → Application → Service Workers
2. 勾选 "Update on reload"
3. 勾选 "Bypass for network"
4. 刷新页面查看效果

---

## 注意事项

⚠️ **关键提醒**:

1. **每次发布必须更新版本号** - 否则用户会一直使用旧缓存
2. **版本号要全局唯一** - 不要重复使用
3. **测试移动端** - PWA主要用于移动设备
4. **考虑回滚** - 保留上一个版本号以便回退

✅ **最佳实践**:

- 使用自动化脚本更新版本号
- 版本号与应用版本同步
- 记录每次发布的缓存版本号
- 定期清理超过3个月的用户缓存

---

## 集成到CI/CD

### GitHub Actions示例

```yaml
- name: Update Cache Version
  run: |
    $version = "v${{ github.run_number }}"
    .\update-cache-version.ps1 -Version $version
  
- name: Build and Publish
  run: dotnet publish -c Release
```

### 版本号自动化

```powershell
# 使用Git commit SHA
$version = "v$(git rev-parse --short HEAD)"

# 使用时间戳
$version = "v$(Get-Date -Format 'yyyyMMddHHmm')"

# 使用版本号
$version = "v1.2.3"
```

---

## 总结

**最简单的发布流程**:

```powershell
# 1. 更新版本
.\update-cache-version.ps1 -Version "v2"

# 2. 编译
dotnet publish -c Release

# 3. 部署
# (上传到服务器)

# 完成! 用户下次访问会自动更新
```

版本号变化 → Service Worker检测更新 → 安装新SW → 激活后删除旧缓存 → 用户获取最新版本
