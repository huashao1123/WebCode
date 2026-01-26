// Textarea resize functionality with localStorage caching
window.textareaResizeManager = {
    // Configuration
    config: {
        minHeight: 72,  // Minimum height: 3 rows of text (approximately 24px per row)
        maxHeight: 400, // Maximum height limit
        storageKey: 'textarea-heights',
        heightTolerance: 1 // Pixel tolerance for height comparison to avoid floating point issues
    },

    // Track if initialized
    _initialized: false,
    _restoredTextareas: new Set(),

    // Initialize textarea resize functionality
    init: function() {
        if (this._initialized) {
            // 如果已初始化，只需恢复高度
            this.restoreHeights();
            return;
        }
        
        // Restore saved heights on page load
        this.restoreHeights();
        
        // Setup observers for all textareas
        this.observeTextareas();
        
        // 监听 Blazor 页面增强事件
        this.setupBlazorListeners();
        
        this._initialized = true;
        console.log('Textarea resize manager initialized');
    },
    
    // 设置 Blazor 监听器
    setupBlazorListeners: function() {
        // 使用 MutationObserver 监听 DOM 变化后恢复高度
        const manager = this;
        const observer = new MutationObserver((mutations) => {
            // 延迟执行以确保 DOM 完全更新
            requestAnimationFrame(() => {
                manager.restoreHeights();
            });
        });
        
        // 只观察关键容器的变化
        const targetNode = document.getElementById('code-assistant-split-container');
        if (targetNode) {
            observer.observe(targetNode, {
                childList: true,
                subtree: true,
                attributes: false
            });
        }
    },

    // Get saved heights from localStorage
    getSavedHeights: function() {
        try {
            const saved = localStorage.getItem(this.config.storageKey);
            return saved ? JSON.parse(saved) : {};
        } catch (e) {
            console.error('Failed to load saved textarea heights:', e);
            return {};
        }
    },

    // Save height to localStorage
    saveHeight: function(textareaId, height) {
        try {
            const heights = this.getSavedHeights();
            heights[textareaId] = height;
            localStorage.setItem(this.config.storageKey, JSON.stringify(heights));
        } catch (e) {
            console.error('Failed to save textarea height:', e);
        }
    },

    // Apply constraints to height
    constrainHeight: function(height) {
        return Math.max(this.config.minHeight, Math.min(height, this.config.maxHeight));
    },

    // Restore saved heights to textareas
    restoreHeights: function() {
        const savedHeights = this.getSavedHeights();
        const manager = this;
        
        Object.keys(savedHeights).forEach(textareaId => {
            const textarea = document.getElementById(textareaId);
            if (textarea) {
                const height = manager.constrainHeight(savedHeights[textareaId]);
                // 使用 !important 确保覆盖内联样式
                textarea.style.setProperty('height', height + 'px', 'important');
                // min-height 始终使用配置的最小值，而不是保存的高度
                textarea.style.setProperty('min-height', manager.config.minHeight + 'px');
                manager._restoredTextareas.add(textareaId);
            }
        });
    },
    
    // 手动恢复指定 textarea 的高度
    restoreTextareaHeight: function(textareaId) {
        const savedHeights = this.getSavedHeights();
        const savedHeight = savedHeights[textareaId];
        
        if (savedHeight) {
            const textarea = document.getElementById(textareaId);
            if (textarea) {
                const height = this.constrainHeight(savedHeight);
                textarea.style.setProperty('height', height + 'px', 'important');
                // min-height 始终使用配置的最小值，而不是保存的高度
                textarea.style.setProperty('min-height', this.config.minHeight + 'px');
                return height;
            }
        }
        return null;
    },

    // Setup ResizeObserver for a textarea
    setupResizeObserver: function(textarea) {
        const textareaId = textarea.id;
        if (!textareaId) return;
        
        const manager = this;
        
        // 先尝试恢复保存的高度
        const savedHeights = this.getSavedHeights();
        const savedHeight = savedHeights[textareaId];
        
        if (savedHeight) {
            const height = this.constrainHeight(savedHeight);
            textarea.style.setProperty('height', height + 'px', 'important');
            // min-height 始终使用配置的最小值
            textarea.style.setProperty('min-height', this.config.minHeight + 'px');
        } else {
            // Apply initial constraints
            const currentHeight = textarea.offsetHeight;
            if (currentHeight) {
                const constrainedHeight = this.constrainHeight(currentHeight);
                if (currentHeight !== constrainedHeight) {
                    textarea.style.setProperty('height', constrainedHeight + 'px');
                }
            }
        }

        // Create ResizeObserver to watch for size changes
        const resizeObserver = new ResizeObserver(entries => {
            for (let entry of entries) {
                const target = entry.target;
                // 使用 offsetHeight 获取完整高度（包含 padding 和 border）
                // 而不是 contentRect.height（只有内容区域高度）
                const newHeight = target.offsetHeight;
                
                // Apply constraints and save
                const constrainedHeight = manager.constrainHeight(newHeight);
                
                // Only update if height changed and needs constraining
                if (Math.abs(newHeight - constrainedHeight) > manager.config.heightTolerance) {
                    target.style.setProperty('height', constrainedHeight + 'px');
                }
                
                // Save to localStorage (with debounce)
                manager.debouncedSave(textareaId, constrainedHeight);
            }
        });

        resizeObserver.observe(textarea);
        
        // Store observer reference for cleanup if needed
        textarea._resizeObserver = resizeObserver;
    },

    // Debounced save function
    debouncedSave: (function() {
        let timeouts = {};
        return function(textareaId, height) {
            if (timeouts[textareaId]) {
                clearTimeout(timeouts[textareaId]);
            }
            timeouts[textareaId] = setTimeout(() => {
                window.textareaResizeManager.saveHeight(textareaId, height);
                console.log('Textarea height saved:', textareaId, height);
            }, 300); // Save 300ms after resize stops
        };
    })(),

    // Observe all textareas with resize enabled
    observeTextareas: function() {
        // Wait for DOM to be ready
        const checkAndSetup = () => {
            const textareas = document.querySelectorAll('textarea[id]');
            textareas.forEach(textarea => {
                const computedStyle = window.getComputedStyle(textarea);
                const resize = computedStyle.resize;
                
                // Only setup for resizable textareas
                if (resize !== 'none' && !textarea._resizeObserver) {
                    this.setupResizeObserver(textarea);
                }
            });
        };

        // Initial setup
        checkAndSetup();

        // Setup MutationObserver to watch for dynamically added textareas
        const observer = new MutationObserver(() => {
            checkAndSetup();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }
};

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.textareaResizeManager.init();
    });
} else {
    // DOM is already ready
    window.textareaResizeManager.init();
}

// Also re-initialize after Blazor renders
if (window.Blazor) {
    window.Blazor.addEventListener('enhancedload', () => {
        // 延迟恢复以确保 DOM 完全更新
        setTimeout(() => {
            window.textareaResizeManager.restoreHeights();
        }, 100);
    });
}

// 提供一个全局方法供 Blazor 调用
window.restoreTextareaHeights = function() {
    if (window.textareaResizeManager) {
        window.textareaResizeManager.restoreHeights();
    }
};

// 提供恢复单个 textarea 高度的方法
window.restoreTextareaHeight = function(textareaId) {
    if (window.textareaResizeManager) {
        return window.textareaResizeManager.restoreTextareaHeight(textareaId);
    }
    return null;
};

console.log('Textarea resize script loaded');
