using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace RassaWeb.Models
{
    public class RassaDatabaseState
    {
        public List<User> Users { get; set; } = new List<User>();
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
        public List<Product> Products { get; set; } = new List<Product>();
        public List<Order> Orders { get; set; } = new List<Order>();
    }

    public class RassaDb
    {
        private static readonly object _lock = new object();
        private static RassaDatabaseState? _state;
        private readonly string _storageFilePath;

        public RassaDb(IConfiguration configuration)
        {
            string appDataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
            if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
            _storageFilePath = Path.Combine(appDataDir, "RassaDB_30Wh_450Prod.json");
        }

        public void EnsureDatabaseInitialized()
        {
            lock (_lock)
            {
                if (_state != null) return;

                if (File.Exists(_storageFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_storageFilePath, Encoding.UTF8);
                        var loaded = JsonSerializer.Deserialize<RassaDatabaseState>(json);
                        if (loaded != null && loaded.Warehouses.Count >= 30 && loaded.Products.Count >= 450)
                        {
                            _state = loaded;
                            return;
                        }
                    }
                    catch { }
                }

                _state = BuildInitialSeedState();
                SaveState();
            }
        }

        private RassaDatabaseState State
        {
            get
            {
                if (_state == null) EnsureDatabaseInitialized();
                return _state!;
            }
        }

        private void SaveState()
        {
            try
            {
                if (_state == null) return;
                var opts = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_state, opts);
                File.WriteAllText(_storageFilePath, json, Encoding.UTF8);
            }
            catch { }
        }

        public static string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        // ==================== 1. المستخدمون وتسجيل أصحاب المخازن والمطاعم ====================

        public User? AuthenticateUser(string emailOrPhone, string password)
        {
            string input = emailOrPhone.Trim().ToLowerInvariant();
            string pass = password.Trim();
            return State.Users.FirstOrDefault(u =>
                u.IsActive &&
                (u.Email.ToLowerInvariant() == input || u.Phone.Trim() == input) &&
                u.PasswordHash == pass);
        }

        public int RegisterUser(User user, string? warehouseName = null, string? warehouseSpecialty = null)
        {
            lock (_lock)
            {
                if (State.Users.Any(u => u.Email.Equals(user.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("Email already exists");
                }

                int nextId = State.Users.Any() ? State.Users.Max(u => u.UserID) + 1 : 1;
                user.UserID = nextId;
                user.Email = user.Email.Trim();
                user.Phone = user.Phone.Trim();
                user.Role = string.IsNullOrEmpty(user.Role) ? "Client" : user.Role;
                user.IsActive = true;
                user.CreatedAt = DateTime.Now;

                State.Users.Add(user);

                // إذا سجل المستخدم كصاحب مخزن (WarehouseManager)، ننشئ له مخزنه تلقائياً مع 15 منتجاً جاهزاً!
                if (user.Role == "WarehouseManager")
                {
                    int nextWhId = State.Warehouses.Any() ? State.Warehouses.Max(w => w.WarehouseID) + 1 : 1;
                    string wName = !string.IsNullOrWhiteSpace(warehouseName) ? warehouseName.Trim() : $"مخزن {user.FullName}";
                    string wSpec = !string.IsNullOrWhiteSpace(warehouseSpecialty) ? warehouseSpecialty.Trim() : "توريدات شاملة للمطاعم والكافيهات";

                    var newWh = new Warehouse
                    {
                        WarehouseID = nextWhId,
                        WarehouseName = wName,
                        Specialty = wSpec,
                        LocationZone = user.LocationZone,
                        DistanceKm = 1.40m,
                        DeliveryStatus = "متاح الآن",
                        ManagerUserID = nextId,
                        ManagerName = user.FullName,
                        IsActive = true
                    };
                    State.Warehouses.Add(newWh);

                    // إضافة 15 منتجاً افتراضياً للمخزن الجديد
                    Seed15ProductsForWarehouse(State, newWh.WarehouseID, newWh.WarehouseName, 1, "متاح الآن");
                }

                SaveState();
                return nextId;
            }
        }

        public User? GetUserById(int userId)
        {
            return State.Users.FirstOrDefault(u => u.UserID == userId);
        }

        public List<User> GetAllUsers()
        {
            return State.Users.OrderByDescending(u => u.CreatedAt).ToList();
        }

        public List<User> GetRestaurantClients()
        {
            return State.Users.Where(u => u.Role == "Client" && u.IsActive).OrderBy(u => u.UserID).ToList();
        }

        public void ToggleUserStatus(int userId)
        {
            lock (_lock)
            {
                var u = State.Users.FirstOrDefault(x => x.UserID == userId);
                if (u != null)
                {
                    u.IsActive = !u.IsActive;
                    SaveState();
                }
            }
        }

        // ==================== 2. الأقسام والمخازن الـ 30 ====================

        public List<Category> GetCategories()
        {
            var cats = State.Categories.OrderBy(c => c.CategoryID).ToList();
            foreach (var c in cats)
            {
                c.ProductCount = State.Products.Count(p => p.CategoryID == c.CategoryID);
            }
            return cats;
        }

        public void AddCategory(string name, string description, string iconClass)
        {
            lock (_lock)
            {
                int nextId = State.Categories.Any() ? State.Categories.Max(c => c.CategoryID) + 1 : 1;
                State.Categories.Add(new Category
                {
                    CategoryID = nextId,
                    CategoryName = name.Trim(),
                    Description = description?.Trim() ?? "",
                    IconClass = string.IsNullOrEmpty(iconClass) ? "bi-box-seam-fill" : iconClass
                });
                SaveState();
            }
        }

        public List<Warehouse> GetWarehouses(string? searchZone = null, string? searchQuery = null)
        {
            var query = State.Warehouses.Where(w => w.IsActive).AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchZone))
            {
                string z = searchZone.Trim();
                query = query.Where(w => w.LocationZone.Contains(z, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                string q = searchQuery.Trim();
                query = query.Where(w =>
                    w.WarehouseName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    w.Specialty.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    w.LocationZone.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var list = query.OrderBy(w => w.DistanceKm).ThenBy(w => w.WarehouseID).ToList();
            foreach (var w in list)
            {
                var mgr = State.Users.FirstOrDefault(u => u.UserID == w.ManagerUserID);
                w.ManagerName = mgr?.FullName ?? "إدارة المخازن";
                w.ProductsCount = State.Products.Count(p => p.WarehouseID == w.WarehouseID);
            }
            return list;
        }

        public Warehouse? GetWarehouseById(int warehouseId)
        {
            var w = State.Warehouses.FirstOrDefault(x => x.WarehouseID == warehouseId);
            if (w != null)
            {
                var mgr = State.Users.FirstOrDefault(u => u.UserID == w.ManagerUserID);
                w.ManagerName = mgr?.FullName ?? "إدارة المخازن";
                w.ProductsCount = State.Products.Count(p => p.WarehouseID == w.WarehouseID);
            }
            return w;
        }

        public Warehouse? GetWarehouseByManager(int managerUserId, int? selectedWarehouseId = null)
        {
            if (selectedWarehouseId.HasValue && selectedWarehouseId.Value > 0)
            {
                var specific = GetWarehouseById(selectedWarehouseId.Value);
                if (specific != null) return specific;
            }

            var wh = State.Warehouses.FirstOrDefault(w => w.ManagerUserID == managerUserId)
                     ?? State.Warehouses.FirstOrDefault();
            return wh != null ? GetWarehouseById(wh.WarehouseID) : null;
        }

        public void AddWarehouse(Warehouse w)
        {
            lock (_lock)
            {
                int nextId = State.Warehouses.Any() ? State.Warehouses.Max(x => x.WarehouseID) + 1 : 1;
                w.WarehouseID = nextId;
                w.ManagerUserID = w.ManagerUserID > 0 ? w.ManagerUserID : 2;
                w.IsActive = true;
                State.Warehouses.Add(w);
                // تعبئة 15 منتجاً تلقائياً لأي مخزن جديد يضاف!
                Seed15ProductsForWarehouse(State, nextId, w.WarehouseName, (nextId % 6) + 1, w.DeliveryStatus);
                SaveState();
            }
        }

        // ==================== 3. المنتجات الـ 450 (15 منتج لكل مخزن) ====================

        public List<Product> GetProducts(int? categoryId = null, int? warehouseId = null, string? search = null, string? sortBy = null)
        {
            var catDict = State.Categories.ToDictionary(c => c.CategoryID);
            var whDict = State.Warehouses.ToDictionary(w => w.WarehouseID);

            foreach (var p in State.Products)
            {
                if (catDict.TryGetValue(p.CategoryID, out var cat))
                {
                    p.CategoryName = cat.CategoryName;
                    p.CategoryIcon = cat.IconClass;
                }
                if (whDict.TryGetValue(p.WarehouseID, out var wh))
                {
                    p.WarehouseName = wh.WarehouseName;
                    p.WarehouseZone = wh.LocationZone;
                    p.WarehouseDistanceKm = wh.DistanceKm;
                    p.WarehouseDeliveryStatus = wh.DeliveryStatus;
                }
            }

            var query = State.Products.AsEnumerable();

            if (categoryId.HasValue && categoryId.Value > 0)
                query = query.Where(p => p.CategoryID == categoryId.Value);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                query = query.Where(p => p.WarehouseID == warehouseId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                query = query.Where(p =>
                    p.ProductName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.WarehouseName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.CategoryName.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            if (sortBy == "price_asc")
                query = query.OrderBy(p => p.Price);
            else if (sortBy == "price_desc")
                query = query.OrderByDescending(p => p.Price);
            else if (sortBy == "distance")
                query = query.OrderBy(p => p.WarehouseDistanceKm).ThenBy(p => p.Price);
            else
                query = query.OrderBy(p => p.WarehouseID).ThenBy(p => p.ProductID);

            return query.ToList();
        }

        public Product? GetProductById(int productId)
        {
            return GetProducts().FirstOrDefault(p => p.ProductID == productId);
        }

        public void AddProductWithInventory(Product p, int initialQty, int minThreshold)
        {
            lock (_lock)
            {
                int nextId = State.Products.Any() ? State.Products.Max(x => x.ProductID) + 1 : 1;
                p.ProductID = nextId;
                p.AvailableQuantity = initialQty;
                p.MinThreshold = minThreshold > 0 ? minThreshold : 10;
                if (string.IsNullOrWhiteSpace(p.ExpectedDeliveryTime))
                    p.ExpectedDeliveryTime = "اليوم - 06:00 مساءً";
                State.Products.Add(p);
                SaveState();
            }
        }

        public void UpdateProductStockAndPrice(int productId, decimal newPrice, int newAvailableQty)
        {
            lock (_lock)
            {
                var prod = State.Products.FirstOrDefault(p => p.ProductID == productId);
                if (prod != null)
                {
                    prod.Price = newPrice;
                    prod.AvailableQuantity = newAvailableQty;
                    SaveState();
                }
            }
        }

        public void DeleteProduct(int productId)
        {
            lock (_lock)
            {
                State.Products.RemoveAll(p => p.ProductID == productId);
                SaveState();
            }
        }

        // ==================== 4. الطلبات والتوريد المباشر من المخازن للمطاعم + Blockchain ====================

        public int CreateOrderWithBlockchain(int clientUserId, int warehouseId, string deliveryAddress, List<CartItem> cartItems)
        {
            lock (_lock)
            {
                int nextOrderId = State.Orders.Any() ? State.Orders.Max(o => o.OrderID) + 1 : 1;
                string orderNumber = (1257 + nextOrderId).ToString();
                decimal totalAmount = cartItems.Sum(i => i.SubTotal);

                var client = State.Users.FirstOrDefault(u => u.UserID == clientUserId) ?? State.Users.First(u => u.Role == "Client");
                var warehouse = State.Warehouses.FirstOrDefault(w => w.WarehouseID == warehouseId) ?? State.Warehouses.First();

                var newOrder = new Order
                {
                    OrderID = nextOrderId,
                    OrderNumber = orderNumber,
                    ClientUserID = client.UserID,
                    ClientName = client.FullName,
                    ClientBusinessType = client.BusinessType,
                    WarehouseID = warehouse.WarehouseID,
                    WarehouseName = warehouse.WarehouseName,
                    OrderDate = DateTime.Now,
                    TotalAmount = totalAmount,
                    OrderStatus = "تم تأكيد الطلب",
                    EstimatedArrivalMinutes = 25,
                    DeliveryAddress = string.IsNullOrWhiteSpace(deliveryAddress) ? client.LocationZone : deliveryAddress,
                    IsChainValid = true
                };

                int itemIdx = 1;
                foreach (var ci in cartItems)
                {
                    newOrder.Items.Add(new OrderItem
                    {
                        OrderItemID = itemIdx++,
                        OrderID = nextOrderId,
                        ProductID = ci.ProductID,
                        ProductName = ci.ProductName,
                        SizeUnit = ci.SizeUnit,
                        Quantity = ci.Quantity,
                        UnitPrice = ci.UnitPrice,
                        SubTotal = ci.SubTotal
                    });

                    var prod = State.Products.FirstOrDefault(p => p.ProductID == ci.ProductID);
                    if (prod != null)
                    {
                        prod.AvailableQuantity = Math.Max(0, prod.AvailableQuantity - ci.Quantity);
                    }
                }

                string genesisPrev = new string('0', 64);
                var b1 = CreateBlock(nextOrderId, 1, "تم إنشاء الطلب", $"كتلة 01: إنشاء الطلب #{orderNumber} لصالح ({client.FullName}) بقيمة {totalAmount:N2} ج.م", genesisPrev);
                var b2 = CreateBlock(nextOrderId, 2, "تم تأكيد الطلب", $"كتلة 02: قبول وتأكيد الطلب #{orderNumber} إلكترونياً من ({warehouse.WarehouseName})", b1.CurrentHash);
                var b3 = CreateBlock(nextOrderId, 3, "تحديث المخزون", $"كتلة 03: خصم كميات الأصناف ({cartItems.Count} أصناف) وتحديث رصيد المخزن تلقائياً", b2.CurrentHash);

                newOrder.LedgerBlocks.Add(b1);
                newOrder.LedgerBlocks.Add(b2);
                newOrder.LedgerBlocks.Add(b3);

                State.Orders.Add(newOrder);
                SaveState();
                return nextOrderId;
            }
        }

        private BlockchainBlock CreateBlock(int orderId, int blockIndex, string eventType, string eventDesc, string prevHash, DateTime? customTime = null)
        {
            DateTime ts = customTime ?? DateTime.Now;
            string payload = $"{orderId}|{blockIndex}|{eventType}|{eventDesc}|{ts:O}|{prevHash}";
            string currHash = ComputeSha256Hash(payload);
            return new BlockchainBlock
            {
                BlockID = (orderId * 10) + blockIndex,
                OrderID = orderId,
                BlockIndex = blockIndex,
                EventType = eventType,
                EventDescription = eventDesc,
                Timestamp = ts,
                PreviousHash = prevHash,
                CurrentHash = currHash
            };
        }

        public void UpdateOrderStatusWithBlockchain(int orderId, string newStatus)
        {
            lock (_lock)
            {
                var ord = State.Orders.FirstOrDefault(o => o.OrderID == orderId);
                if (ord != null)
                {
                    ord.OrderStatus = newStatus;
                    if (newStatus == "تم التسليم") ord.EstimatedArrivalMinutes = 0;

                    var lastBlock = ord.LedgerBlocks.OrderByDescending(b => b.BlockIndex).FirstOrDefault();
                    int nextIdx = (lastBlock?.BlockIndex ?? 0) + 1;
                    string prevHash = lastBlock?.CurrentHash ?? new string('0', 64);

                    string eventType = newStatus switch
                    {
                        "المخزن يجهّز الطلب" => "تجهيز الطلب",
                        "جاهز للاستلام" => "تجهيز الطلب",
                        "في الطريق" => "شحن الطلب",
                        "تم التسليم" => "تسليم الطلب",
                        _ => newStatus
                    };

                    var newBlock = CreateBlock(orderId, nextIdx, eventType, $"كتلة 0{nextIdx}: تحديث حالة الطلب #{ord.OrderNumber} إلى ({newStatus}) وتوثيق الحدث", prevHash);
                    ord.LedgerBlocks.Add(newBlock);
                    SaveState();
                }
            }
        }

        public List<Order> GetOrders(int? clientUserId = null, int? warehouseId = null)
        {
            var query = State.Orders.AsEnumerable();
            if (clientUserId.HasValue)
                query = query.Where(o => o.ClientUserID == clientUserId.Value);
            if (warehouseId.HasValue)
                query = query.Where(o => o.WarehouseID == warehouseId.Value);
            return query.OrderByDescending(o => o.OrderDate).ToList();
        }

        public Order? GetOrderDetails(int orderId)
        {
            var ord = State.Orders.FirstOrDefault(o => o.OrderID == orderId);
            if (ord != null)
            {
                ord.IsChainValid = true;
                for (int i = 1; i < ord.LedgerBlocks.Count; i++)
                {
                    if (ord.LedgerBlocks[i].PreviousHash != ord.LedgerBlocks[i - 1].CurrentHash)
                    {
                        ord.IsChainValid = false;
                        break;
                    }
                }
            }
            return ord;
        }

        public DashboardStats GetDashboardStats()
        {
            return new DashboardStats
            {
                TotalClients = State.Users.Count(u => u.Role == "Client"),
                TotalWarehouses = State.Warehouses.Count(w => w.IsActive),
                TotalCategories = State.Categories.Count,
                TotalProducts = State.Products.Count,
                TotalOrders = State.Orders.Count,
                TotalSales = State.Orders.Sum(o => o.TotalAmount),
                LowStockCount = State.Products.Count(p => p.AvailableQuantity <= p.MinThreshold),
                TotalBlockchainBlocks = State.Orders.Sum(o => o.LedgerBlocks.Count)
            };
        }

        // ==================== 5. البيانات التأسيسية (30 مخزناً × 15 منتجاً = 450 منتجاً) ====================

        private static readonly Dictionary<int, (string Name, string Desc, string Size, decimal Price)[]> CategoryTemplates15 = new()
        {
            [1] = new[]
            {
                ("حبوب بن برازيلي فاخر (Medium Roast)", "حبوب بن أرابيكا برازيلي محمصة طازجة للإسبريسو والقهوة المختصة", "1 كجم", 480.00m),
                ("حبوب بن برازيلي فاخر (عبوة توفير للمقاهي)", "عبوة جملة اقتصادية للمقاهي عالية الاستهلاك - تحميص متوسط", "5 كجم", 2250.00m),
                ("حبوب بن برازيلي (عبوة تجريبية)", "عبوة 500 جرام لتجربة التحميص والمذاق", "500 جم", 250.00m),
                ("حبوب بن كولومبي مختص (Supremo)", "بن كولومبي 100% أرابيكا بإيحاءات الشوكولاتة والمكسرات", "1 كجم", 590.00m),
                ("حبوب بن إثيوبي ييرجاشيف (Specialty)", "بن إثيوبي مختص للقهوة المقطرة V60 والإسبريسو الفاكهي", "1 كجم", 640.00m),
                ("بن إسبريسو إيطالي كلاسيك (Dark Roast)", "خلطة إيطالية غنية بالكريمة (80% أرابيكا / 20% روبوستا)", "1 كجم", 460.00m),
                ("بن إسبريسو إيطالي جملة للمقاهي", "عبوة اقتصادية لتحضير الكابتشينو واللاتيه والأمريكانو", "3 كجم", 1320.00m),
                ("بن تركي محوج فاخر بالحبهان", "بن تركي مطحون ناعم طازج بخلطة الحبهان الأصلية", "1 كجم", 430.00m),
                ("بن تركي سادة فاتح ووسط للكافيهات", "بن تركي صافي عالي الجودة بوش كثيف", "2 كجم", 820.00m),
                ("مسحوق هوت شوكليت إيطالي كريمي", "بودرة شوكولاتة ساخنة كثيفة القوام سهلة التحضير على البخار", "2 كجم", 420.00m),
                ("مسحوق وايت موكا وفرابتشينو بودر", "بودرة أساس المشروبات المثلجة والساخنة للكافيهات", "1.5 كجم", 390.00m),
                ("شاي أسود سيلاني فاخر خرز", "شاي سيلاني أصلي سريع التخمير بلون أحمر داكن", "5 كجم", 780.00m),
                ("شاي أخضر طبيعي بالنعناع (فتلة جملة)", "كرتونة شاي أخضر مغلف للمطاعم والكافيهات والفنادق", "1000 فتلة", 610.00m),
                ("بودرة ماتشا يابانية أصلية للاتيه", "ماتشا خضراء نقية للمشروبات الساخنة والآيس ماتشا", "500 جم", 750.00m),
                ("قهوة سريعة الذوبان جولد (Gold Bulk)", "حبيبات قهوة سريعة الذوبان لتحضير النسكافيه والآيس كوفي", "2 كجم", 920.00m)
            },
            [2] = new[]
            {
                ("أكواب ورقية دبل وول 8 أونصة بالأغطية", "أكواب ورقية بطبقتين عازلة للحرارة للمشروبات الساخنة", "500 كوب", 650.00m),
                ("أكواب ورقية دبل وول 12 أونصة بالأغطية", "أكواب ورقية متينة لمشروبات الكابتشينو واللاتيه", "500 كوب", 790.00m),
                ("أكواب ورقية سنجل 4 أونصة للإسبريسو", "أكواب صغيرة مخصصة للإسبريسو والقهوة التركي", "1000 كوب", 520.00m),
                ("أكواب بلاستيك PET شفافة 12 أونصة بغطاء قبة", "أكواب فائقة الشفافية للآيس كوفي والعصائر", "1000 كوب", 980.00m),
                ("أكواب بلاستيك PET شفافة 16 أونصة بغطاء فلات", "أكواب كبيرة للمشروبات الباردة والسموذي", "1000 كوب", 1150.00m),
                ("علب وجبات كرافت صحية مبطنة (حجم وسط)", "علب كرافت غذائية مقاومة للدهون والصوصات", "200 علبة", 420.00m),
                ("علب وجبات كرافت مقسمة 3 عيون للمطاعم", "علب تيك أواي متينة للوجبات المتكاملة والأرز والمشويات", "200 علبة", 560.00m),
                ("علب برجر كرافت مطبوعة عالية المتانة", "علب برجر صحية تحفظ الحرارة ولا تتأثر بالصوصات", "500 علبة", 680.00m),
                ("علب بيتزا كرتون مضلع مقاس لارج", "علب بيتزا قوية بفتحات تهوية تحفظ القرمشة", "200 علبة", 740.00m),
                ("أكياس كرافت ورقية بمقابض مجدولة (حجم وسط)", "أكياس كرافت أنيقة لتسليم طلبات الدليفري والتيك أواي", "250 كيس", 375.00m),
                ("أكياس كرافت ورقية عريضة لعلب الوجبات", "أكياس بقاعدة عريضة تتحمل الأوزان الثقيلة", "200 كيس", 410.00m),
                ("شفاطات عصير وآيس كوفي عريضة مغلفة", "شفاطات سوداء مغلفة فردياً لضمان النظافة", "2000 قطعة", 340.00m),
                ("حامل أكواب كرتون رباعي (Cup Carrier)", "حامل أكواب متين لتوصيل 4 مشروبات بأمان", "250 حامل", 460.00m),
                ("مناديل سفرة مطاعم وكافيهات فاخرة", "كرتونة مناديل ورقية ناعمة عالية الامتصاص", "20 باكت", 530.00m),
                ("رول فويل ألومنيوم واسترتش غذائي للمطابخ", "طقم تغليف حراري وحفظ أغذية للمطاعم الكبرى", "كرتونة 6 رول", 890.00m)
            },
            [3] = new[]
            {
                ("صوص كراميل مملح للقهوة والحلويات", "صوص كراميل غني القوام للمشروبات والوافل بمضخة", "2.5 كجم", 380.00m),
                ("صوص شوكولاتة بلجيكي داكن (Dark Sauce)", "صوص شوكولاتة مركز للموكا وتزيين الحلويات", "2.5 كجم", 395.00m),
                ("صوص شوكولاتة بيضاء كريمي (White Sauce)", "صوص وايت موكا غني لمشروبات القهوة والبان كيك", "2.5 كجم", 410.00m),
                ("صوص فستق إيطالي طبيعي (Pistachio Paste)", "زبدة وصوص فستق ناعم للبيستاشيو لاتيه والكرواسون", "3 كجم", 950.00m),
                ("صوص لوتس بيسكوف كرانشي للحلويات", "صوص بسكويت اللوتس الأصلي للكريب والوافل والميلك شيك", "3 كجم", 780.00m),
                ("سيرب فانيليا فرنسية للكافيهات", "سيرب نكهة الفانيليا المركز للقهوة المثلجة واللاتيه", "1 لتر", 240.00m),
                ("سيرب بندق محمص (Hazelnut Syrup)", "سيرب بندق مركز لمشروبات الإسبريسو والقهوة الباردة", "1 لتر", 245.00m),
                ("سيرب كراميل كلاسيك للمشروبات", "سيرب كراميل نقي سريع الذوبان في المشروبات الباردة", "1 لتر", 240.00m),
                ("سيرب نعناع أزرق وموهيتو (Mojito Syrup)", "سيرب منعش لتحضير الموهيتو والصودا الإيطالية", "1 لتر", 230.00m),
                ("بيوريه فراولة وتوت طبيعي للسموذي", "مركز فواكه طبيعي لتحضير السموذي والميلك شيك", "1.5 كجم", 320.00m),
                ("صوص باربكيو مدخن للمطاعم (Smoky BBQ)", "صوص باربكيو أصلي للمشويات والبرجر والسندوتشات", "5 كجم", 310.00m),
                ("صوص جبنة شيدر كريمي للمطاعم والبطاطس", "صوص شيدر جاهز للتسخين والتقديم على البطاطس والبرجر", "4 كجم", 410.00m),
                ("صوص رانش أمريكي بالثوم والأعشاب", "صوص رانش كريمي للبيتزا والسلطات والدجاج المقرمش", "4 كجم", 390.00m),
                ("مايونيز جملة ثقيل القوام للمطاعم", "مايونيز أصلي لتحضير السندوتشات والسلطات والصوصات", "5 كجم", 360.00m),
                ("كاتشب طماطم طبيعي جملة للمطاعم", "كاتشب طماطم مركز + باكتات صغيرة للتيك أواي", "5 كجم", 280.00m)
            },
            [4] = new[]
            {
                ("حليب باريستا كامل الدسم (كرتونة 12 لتر)", "حليب مخصص للتبخير والرسم على اللاتيه برغوة ثابتة", "12 لتر", 540.00m),
                ("حليب خالي الدسم للكافيهات (كرتونة 12 لتر)", "حليب خالي الدسم للمشروبات الصحية والدايت", "12 لتر", 520.00m),
                ("حليب نصف دسم طويل الأجل للمقاهي", "حليب متوازن القوام للكابتشينو والقهوة بالحليب", "12 لتر", 530.00m),
                ("كريمة خفق سائلة للحلويات والمشروبات 35%", "كريمة طبيعية غنية للخفق والتزيين والوافل", "12 لتر", 1680.00m),
                ("كريمة طهي سائلة للمطاعم والباستا", "كريمة طهي متماسكة لا تفصل مع الحرارة العالية", "12 لتر", 1590.00m),
                ("حليب شوفان باريستا نباتي (Oat Milk)", "بديل الحليب النباتي الأكثر طلباً في المقاهي المختصة", "6 لتر", 690.00m),
                ("حليب لوز غير محلى للكافيهات (Almond Milk)", "حليب لوز طبيعي منخفض السعرات قابل للتبخير", "6 لتر", 720.00m),
                ("حليب جوز هند باريستا للمشروبات الاستوائية", "حليب جوز هند كريمي للقهوة الباردة والسموذي", "6 لتر", 710.00m),
                ("حليب مكثف محلى جملة (للسبانيش لاتيه)", "حليب مكثف بقوام مثالي للسبانيش لاتيه والحلويات", "5 كجم", 490.00m),
                ("حليب مبخر غير محلى للشاي الكرك والقهوة", "حليب مبخر مركز لتحضير شاي الكرك الأصلي", "كرتونة 48 علبة", 960.00m),
                ("قشطة طبيعية معقمة للحلويات والمطاعم", "قشطة غنية القوام للكنافة والوافل والحلويات الشرقية", "كرتونة 24 عبوة", 740.00m),
                ("جبنة كريمي قابلة للدهن والتشيز كيك", "بلوك جبنة كريمي طبيعية للتشيز كيك والسندوتشات", "4 كجم", 680.00m),
                ("لبن رايب وزبادي يوناني جملة للكافيهات", "زبادي يوناني كثيف لتحضير السموذي الصحي والفطور", "5 كجم", 350.00m),
                ("آيس كريم فانيليا وشوكولاتة جملة", "جالون آيس كريم إيطالي لتقديمه مع الوافل والمولتن كيك", "جالون 4 لتر", 420.00m),
                ("بخاخ كريمة مخفوقة جاهزة للتزيين", "عبوات كريمة خفق فورية لتزيين الفرابتشينو والآيس كوفي", "كرتونة 12 عبوة", 880.00m)
            },
            [5] = new[]
            {
                ("دقيق مخبوزات وبيتزا فاخر استخراج 72%", "دقيق عالي البروتين مثالي للبيتزا الإيطالية والكرواسون", "جوال 25 كجم", 620.00m),
                ("دقيق حلويات وكيك أبيض فائق النعومة", "دقيق مخصص للكيك والوافل والبان كيك والجاتوه", "جوال 25 كجم", 640.00m),
                ("دقيق قمح كامل وعيش صحي للمخابز", "دقيق حبة كاملة غني بالألياف للمخبوزات الصحية", "جوال 20 كجم", 580.00m),
                ("زبدة نيوزيلندي طبيعية غير مملحة", "زبدة طبيعية 82% دسم لتوريق الكرواسون والباتيه", "قالب 5 كجم", 1450.00m),
                ("زبدة توريق مارجرين فاخرة للمخابز", "زبدة مرنة مخصصة لفرد العجائن والميل فاي والباتيه", "كرتونة 10 كجم", 920.00m),
                ("خميرة فورية جافة سريعة التفاعل", "خميرة جافة عالية النشاط لجميع أنواع العجائن", "كرتونة 10 كجم", 890.00m),
                ("محسن خبز ومخبوزات بودر للمخابز", "محسن عجين يمنح طراوة وحجماً مثالياً للمخبوزات", "5 كجم", 460.00m),
                ("بيكنج بودر وفانيليا خام جملة للحلواني", "عبوة توفير للمخابز ومحلات الحلويات والكافيهات", "5 كجم", 380.00m),
                ("شوكولاتة خام بالحليب والدارك للخبز والحشو", "قوالب شوكولاتة خام لحشو الكرواسون والمولتن كيك", "5 كجم", 720.00m),
                ("رقائق شوكولاتة حرارية (Chocolate Chips)", "شوكولاتة شيبس لا تذوب في الفرن للكوكيز والمافن", "4 كجم", 690.00m),
                ("كاكاو خام أسباني داكن غير محلى", "بودرة كاكاو خام عالية الدسم للبراونيز والكيك", "5 كجم", 840.00m),
                ("سكر أبيض ناعم نقي للمقاهي والمخابز", "سكر أبيض ناعم سريع الذوبان للمشروبات والحلويات", "شيكارة 25 كجم", 850.00m),
                ("سكر بني رطب للكوكيز والقهوة المختصة", "سكر بني طبيعي يمنح الكوكيز طراوة ومذاق كراميل", "10 كجم", 490.00m),
                ("خلطة وافل وبان كيك وكريب جاهزة للكافيهات", "خلطة بودرة سريعة التحضير بقوام مقرمش وذهبي", "10 كجم", 670.00m),
                ("مكسرات مجروشة (بندق ولوز وفستق) للتزيين", "تشكيلة مكسرات محمصة لتزيين الحلويات والمشروبات", "3 كجم", 1150.00m)
            },
            [6] = new[]
            {
                ("بطاطس نصف مقلية مقرمشة للمطاعم (9mm)", "بطاطس جملة مقطعة ومجهزة للقلي السريع بقرمشة تدوم", "كرتونة 10 كجم", 580.00m),
                ("بطاطس ويدجز متبلة نصف مقلية (Wedges)", "بطاطس ويدجز بقشرها متبلة بالأعشاب للمطاعم والكافيهات", "كرتونة 10 كجم", 640.00m),
                ("برجر بقري صافي متبل للمطاعم (150 جم)", "قطع برجر بقري صافي جاهزة للشوي على الجريل", "كرتونة 5 كجم", 1350.00m),
                ("برجر بقري سماش طازج للمطاعم (Smash Burger)", "كرات لحم بقري بنسبة دهون متوازنة للسماش برجر", "5 كجم", 1420.00m),
                ("صدور دجاج فيليه مبردة صافية", "صدور دجاج طازجة مخلية للشوي والشاورما والباستا", "5 كجم", 1180.00m),
                ("استربس دجاج كرسبي حار وعادي (Chicken Strips)", "أصابع دجاج فيليه متبلة ومغطاة بطبقة مقرمشة", "5 كجم", 1250.00m),
                ("جبنة موزاريلا طبيعية مبشورة للبيتزا", "جبنة موزاريلا طبيعية 100% بمطة عالية ولون ذهبي", "كيس 5 كجم", 980.00m),
                ("جبنة شيدر حمراء وصفراء شرائح للبرجر", "شرائح جبنة شيدر سريعة الذوبان على البرجر والسندوتشات", "قالب 4 كجم", 890.00m),
                ("أصابع موزاريلا مقرمشة (Mozzarella Sticks)", "مقبلات أصابع الجبنة الموزاريلا المغطاة بالبقسماط", "3 كجم", 720.00m),
                ("حلقات بصل مقرمشة للمطاعم (Onion Rings)", "حلقات بصل ذهبية جاهزة للقلي السريع كمقبلات", "5 كجم", 480.00m),
                ("بيبروني بقري مدخن شرائح للبيتزا", "شرائح بيبروني بقري متبلة للبيتزا والفطائر", "2.5 كجم", 690.00m),
                ("هوت دوج وسوسيس بقري فاخر للمطاعم", "هوت دوج بقري مدخن للسندوتشات والبيتزا", "5 كجم", 950.00m),
                ("زيت قلي نباتي نقي يتحمل الحرارة العالية", "جركن زيت قلي مخصص للمطاعم والفراير العميق", "جركن 18 لتر", 1290.00m),
                ("عيش برجر بريوش وتورتيلا طازج للمطاعم", "كرتونة خبز بريوش طري وتورتيلا للشاورما والراب", "كرتونة 100 قطعة", 450.00m),
                ("خضروات مجمدة ومشروم شرائح للمطابخ", "مشروم شرائح وذرة حلوة وهالبينو مخلل للبيتزا والبرجر", "كرتونة 6 كجم", 540.00m)
            }
        };

        private static void Seed15ProductsForWarehouse(RassaDatabaseState state, int warehouseId, string warehouseName, int primaryCategoryId, string deliveryStatus)
        {
            if (!CategoryTemplates15.TryGetValue(primaryCategoryId, out var templates))
                templates = CategoryTemplates15[1];

            int nextProdId = state.Products.Any() ? state.Products.Max(p => p.ProductID) + 1 : 1;
            decimal priceDelta = ((warehouseId - 1) % 5) * 5.0m;
            string delivTime = deliveryStatus == "متاح الآن" ? "اليوم - 06:00 مساءً" : "اليوم - 08:00 مساءً";

            for (int i = 0; i < templates.Length; i++)
            {
                var t = templates[i];
                decimal finalPrice = (warehouseId == 1 && i == 0) ? t.Price : (t.Price + priceDelta);
                int pid = nextProdId++;
                int availQty = pid == 1 ? 24 : (pid % 9 == 0 ? 8 : 30 + (pid % 40));

                state.Products.Add(new Product
                {
                    ProductID = pid,
                    ProductName = t.Name,
                    Description = $"{t.Desc} - توريد ({warehouseName})",
                    SizeUnit = t.Size,
                    Price = finalPrice,
                    ExpectedDeliveryTime = delivTime,
                    CategoryID = primaryCategoryId,
                    WarehouseID = warehouseId,
                    AvailableQuantity = availQty,
                    MinThreshold = 10
                });
            }
        }

        private RassaDatabaseState BuildInitialSeedState()
        {
            var s = new RassaDatabaseState();

            s.Users = new List<User>
            {
                new User { UserID = 1, FullName = "صلاح محمد صلاح (مسؤول منصة رِصّة)", Phone = "01000000001", Email = "admin@rassa.com", PasswordHash = "123456", Role = "Admin", BusinessType = "إدارة المنصة", LocationZone = "القاهرة - مدينة نصر", IsActive = true, CreatedAt = DateTime.Now.AddDays(-30) },
                new User { UserID = 2, FullName = "أحمد محمود - مدير مخازن القهوة والباريستا", Phone = "01000000002", Email = "coffee.wh@rassa.com", PasswordHash = "123456", Role = "WarehouseManager", BusinessType = "مخزن توريد", LocationZone = "القاهرة - مدينة نصر", IsActive = true, CreatedAt = DateTime.Now.AddDays(-25) },
                new User { UserID = 3, FullName = "خالد سعيد - مدير مخازن التغليف والأكواب", Phone = "01000000003", Email = "pack.wh@rassa.com", PasswordHash = "123456", Role = "WarehouseManager", BusinessType = "مخزن توريد", LocationZone = "القاهرة - المعادي", IsActive = true, CreatedAt = DateTime.Now.AddDays(-20) },
                new User { UserID = 4, FullName = "مصطفى حسن - مدير مخازن الصوصات والألبان", Phone = "01000000004", Email = "sauce.wh@rassa.com", PasswordHash = "123456", Role = "WarehouseManager", BusinessType = "مخزن توريد", LocationZone = "الجيزة - الدقي والمهندسين", IsActive = true, CreatedAt = DateTime.Now.AddDays(-18) },
                new User { UserID = 5, FullName = "كافيه أرابيكا المختص (Arabica Specialty Cafe)", Phone = "01011112222", Email = "cafe@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "كافيه", LocationZone = "القاهرة - مدينة نصر", IsActive = true, CreatedAt = DateTime.Now.AddDays(-15) },
                new User { UserID = 6, FullName = "مطعم الشرق للمشويات والبرجر", Phone = "01033334444", Email = "rest@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "مطعم", LocationZone = "القاهرة - المعادي", IsActive = true, CreatedAt = DateTime.Now.AddDays(-12) },
                new User { UserID = 7, FullName = "مخبز وحلواني السنابل الذهبية", Phone = "01055556666", Email = "bakery@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "مخبز", LocationZone = "الجيزة - الدقي", IsActive = true, CreatedAt = DateTime.Now.AddDays(-10) },
                new User { UserID = 8, FullName = "كافيه لاونج النيل (Nile Lounge)", Phone = "01077778888", Email = "nile@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "كافيه", LocationZone = "أسيوط - شارع الجمهورية", IsActive = true, CreatedAt = DateTime.Now.AddDays(-7) },
                new User { UserID = 9, FullName = "مطعم وكافيه السرايا الملكية", Phone = "01099990000", Email = "saraya@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "مطعم", LocationZone = "الإسكندرية - سموحة", IsActive = true, CreatedAt = DateTime.Now.AddDays(-5) },
                new User { UserID = 10, FullName = "كافيه وباتيسري لا باريس (La Paris)", Phone = "01122223333", Email = "laparis@rassa.com", PasswordHash = "123456", Role = "Client", BusinessType = "كافيه", LocationZone = "القاهرة - التجمع الخامس", IsActive = true, CreatedAt = DateTime.Now.AddDays(-3) }
            };

            s.Categories = new List<Category>
            {
                new Category { CategoryID = 1, CategoryName = "مستلزمات القهوة والمشروبات الساخنة", Description = "حبوب بن برازيلي وكولومبي، إسبريسو، شوكولاتة ساخنة، وشاي فاخر للمقاهي", IconClass = "bi-cup-hot-fill" },
                new Category { CategoryID = 2, CategoryName = "مواد وعبوات التغليف والأكواب", Description = "أكواب ورقية دبل وول، أكواب آيس كوفي PET، علب وجبات كرافت، وأكياس مطبوعة", IconClass = "bi-box-seam-fill" },
                new Category { CategoryID = 3, CategoryName = "الصوصات والسيرب والنكهات", Description = "صوصات الكافيهات (كراميل، شوكولاتة، فستق)، سيرب نكهات، وصوصات المطاعم (باربكيو، شيدر)", IconClass = "bi-eyedropper" },
                new Category { CategoryID = 4, CategoryName = "الألبان والكريمة وبدائل الحليب", Description = "حليب باريستا للتبخير، كريمة خفق وطهي، حليب مكثف، وحليب شوفان نباتي", IconClass = "bi-droplet-half" },
                new Category { CategoryID = 5, CategoryName = "خامات المخابز والحلويات والدقيق", Description = "دقيق مخبوزات وبيتزا فاخر، زبدة نيوزيلندي، خميرة فورية، شوكولاتة خام، وسكر", IconClass = "bi-basket2-fill" },
                new Category { CategoryID = 6, CategoryName = "المجمدات واللحوم والجبن للمطاعم", Description = "بطاطس نصف مقلية، برجر بقري، فيليه دجاج، وجبنة موزاريلا طبيعية للمطاعم", IconClass = "bi-snow2" }
            };

            var warehouseSeeds = new (int Id, string Name, string Spec, string Zone, decimal Dist, string Status, int Mgr, int CatId)[]
            {
                (1, "مخزن مستلزمات القهوة العالمية", "توريد البن البرازيلي والكولومبي ومستلزمات البارستا", "القاهرة - مدينة نصر", 1.1m, "متاح الآن", 2, 1),
                (2, "مؤسسة أرابيكا لتوريد البن المختص", "بن إسبريسو إيطالي وقهوة مختصة للمقاهي والفنادق", "القاهرة - مصر الجديدة", 1.5m, "متاح الآن", 2, 1),
                (3, "شركة النيل لتوريدات الكافيهات والمشروبات", "توريد البن والشاي الفاخر والهوت شوكليت", "الجيزة - المهندسين", 2.0m, "توصيل اليوم", 2, 1),
                (4, "مخزن باريستا برو (Barista Pro Hub)", "جميع خامات القهوة الساخنة والباردة والماتشا", "القاهرة - التجمع الخامس", 2.4m, "متاح الآن", 2, 1),
                (5, "مخازن البن الذهبي للصعيد", "توريد البن المحمص والشاي السيلاني للمقاهي", "أسيوط - شارع الجمهورية", 1.2m, "متاح الآن", 2, 1),

                (6, "متجر التغليف المتكامل (PackPro)", "أكواب ورقية وبلاستيكية وعبوات كرافت للمطاعم والكافيهات", "القاهرة - مدينة نصر", 1.3m, "توصيل اليوم", 3, 2),
                (7, "مخزن إيكو باك (EcoPack) للتغليف الصحي", "عبوات تغليف صديقة للبيئة وأكواب دبل وول مطبوعة", "الجيزة - 6 أكتوبر", 2.2m, "متاح الآن", 3, 2),
                (8, "المصرية العالمية للأكواب والعبوات الغذائية", "أكواب PET وعلب وجبات وأكياس تيك أواي", "القاهرة - المعادي", 2.6m, "متاح الآن", 3, 2),
                (9, "مخزن كرافت ماستر لتغليف المطاعم", "علب بيتزا وبرجر وأكياس كرافت بمقابض", "الإسكندرية - سموحة", 1.9m, "توصيل اليوم", 3, 2),
                (10, "مخزن الصعيد الحديث للتغليف والأكواب", "توريد شامل للأكواب والمناديل والعبوات للمطاعم", "أسيوط - المنطقة الصناعية", 1.4m, "متاح الآن", 3, 2),

                (11, "مخزن الصوصات والمكونات الذهبية", "توريد الصوصات العالمية والسيرب والإضافات الغذائية", "القاهرة - المعادي", 1.6m, "متاح الآن", 4, 3),
                (12, "شركة فليفرز (Flavors) للسيرب والتوبينج", "سيرب نكهات فرنسي وصوصات حلوة للكافيهات", "القاهرة - مدينة نصر", 2.1m, "متاح الآن", 4, 3),
                (13, "مخزن ماستر صوص لتوريدات المطاعم", "صوصات باربكيو، رانش، شيدر، ومايونيز جملة", "الجيزة - الدقي", 2.5m, "توصيل اليوم", 4, 3),
                (14, "مؤسسة الذواقة للصوصات والتوابل", "صوصات برجر وبيتزا وتوابل مطاعم عالمية", "المنصورة - حي الجامعة", 1.8m, "متاح الآن", 4, 3),
                (15, "مخزن الوادي للصوصات والنكهات المركزة", "توريد صوصات الفستق والكراميل والشوكولاتة", "المنيا - كورنيش النيل", 2.3m, "توصيل اليوم", 4, 3),

                (16, "مؤسسة المراعي للألبان والكريمة", "توريد حليب الباريستا والكريمة والجبن للمقاهي والمطاعم", "الجيزة - الدقي", 1.7m, "متاح الآن", 4, 4),
                (17, "مخازن ديري لاند (Dairy Land) للكافيهات", "حليب كامل الدسم وبدائل حليب نباتية (شوفان ولوز)", "القاهرة - التجمع الخامس", 2.2m, "متاح الآن", 4, 4),
                (18, "شركة الخير لتوريد منتجات الألبان والزبدة", "حليب مكثف، قشطة، وكريمة خفق وطهي للمطاعم", "القاهرة - شبرا", 2.9m, "توصيل اليوم", 4, 4),
                (19, "مخزن الدلتا للألبان والجبن الطبيعي", "توريد الألبان الطازجة والكريمة السائلة للمقاهي", "طنطا - شارع البحر", 1.9m, "متاح الآن", 4, 4),
                (20, "مخزن النيل الأزرق للألبان المبردة", "توريد يومي لحليب الباريستا والكريمة للمطاعم", "سوهاج - شارع أسيوط سوهاج", 1.6m, "متاح الآن", 4, 4),

                (21, "مخازن السنابل للدقيق ومستلزمات المخابز", "توريد الدقيق الفاخر والزبدة والخميرة وخامات الحلواني", "القاهرة - مصر الجديدة", 1.8m, "توصيل اليوم", 2, 5),
                (22, "شركة المطاحن الذهبية لتوريد المخابز", "دقيق استخراج 72% ودقيق بيتزا ومحسنات خبز", "القاهرة - العاشر من رمضان", 3.1m, "متاح الآن", 2, 5),
                (23, "مخزن شيف باسترى (Chef Pastry) للحلواني", "شوكولاتة خام، زبدة نيوزيلندي، كاكاو، وسكر بودرة", "الجيزة - المهندسين", 2.3m, "متاح الآن", 2, 5),
                (24, "مؤسسة الأهرام لخامات المخابز والحلويات", "توريد الجملة للدقيق والسكر والخميرة والفانيليا", "الإسكندرية - محرم بك", 2.7m, "توصيل اليوم", 2, 5),
                (25, "مخزن الصعيد المتحد للدقيق وخامات الخَبز", "توريد أجولة الدقيق والسكر والزبدة للمخابز", "أسيوط - الوليدية", 1.3m, "متاح الآن", 2, 5),

                (26, "مخزن الذواقة للمجمدات وتوريدات المطاعم", "توريد البطاطس واللحوم والدواجن والموزاريلا للمطاعم", "القاهرة - التجمع الخامس", 2.0m, "متاح الآن", 3, 6),
                (27, "شركة فريش فروزن (Fresh Frozen) للأغذية", "بطاطس نصف مقلية وبرجر بقري واستربس دجاج", "الجيزة - 6 أكتوبر", 2.8m, "متاح الآن", 3, 6),
                (28, "مخازن الشرق للحوم والدواجن المبردة", "توريد فيليه دجاج وشاورما ولحوم مفرومة للمطاعم", "القاهرة - مدينة نصر", 1.5m, "توصيل اليوم", 3, 6),
                (29, "مؤسسة البحر المتوسط للمجمدات والزيوت", "زيوت قلي جملة، جبنة موزاريلا، وخضروات مجمدة", "بورسعيد - الحي الإفرنجي", 2.4m, "متاح الآن", 3, 6),
                (30, "مخزن النخبة لتوريدات المطاعم والفنادق", "توريد متكامل للحوم والمجمدات والجبن والبطاطس", "الأقصر - شارع التلفزيون", 1.9m, "توصيل اليوم", 3, 6)
            };

            foreach (var ws in warehouseSeeds)
            {
                s.Warehouses.Add(new Warehouse
                {
                    WarehouseID = ws.Id,
                    WarehouseName = ws.Name,
                    Specialty = ws.Spec,
                    LocationZone = ws.Zone,
                    DistanceKm = ws.Dist,
                    DeliveryStatus = ws.Status,
                    ManagerUserID = ws.Mgr,
                    IsActive = true
                });

                // تعبئة 15 منتجاً داخل كل مخزن (30 × 15 = 450 منتجاً)
                Seed15ProductsForWarehouse(s, ws.Id, ws.Name, ws.CatId, ws.Status);
            }

            // الطلب التجريبي الأول رقم #1258
            var ord1 = new Order
            {
                OrderID = 1,
                OrderNumber = "1258",
                ClientUserID = 5,
                ClientName = "كافيه أرابيكا المختص (Arabica Specialty Cafe)",
                ClientBusinessType = "كافيه",
                WarehouseID = 1,
                WarehouseName = "مخزن مستلزمات القهوة العالمية",
                OrderDate = DateTime.Now.AddMinutes(-40),
                TotalAmount = 8900.00m,
                OrderStatus = "جاهز للاستلام",
                EstimatedArrivalMinutes = 25,
                DeliveryAddress = "القاهرة - مدينة نصر - شارع عباس العقاد - كافيه أرابيكا",
                IsChainValid = true,
                Items = new List<OrderItem>
                {
                    new OrderItem { OrderItemID = 1, OrderID = 1, ProductID = 1, ProductName = "حبوب بن برازيلي فاخر (Medium Roast)", SizeUnit = "1 كجم", Quantity = 5, UnitPrice = 480.00m, SubTotal = 2400.00m },
                    new OrderItem { OrderItemID = 2, OrderID = 1, ProductID = 76, ProductName = "أكواب ورقية دبل وول 8 أونصة بالأغطية", SizeUnit = "500 كوب", Quantity = 10, UnitPrice = 650.00m, SubTotal = 6500.00m }
                }
            };

            string g0 = new string('0', 64);
            var ob1 = CreateBlock(1, 1, "تم إنشاء الطلب", "كتلة 01: إنشاء الطلب #1258 بواسطة كافيه أرابيكا بقيمة 8,900.00 ج.م", g0, DateTime.Now.AddMinutes(-40));
            var ob2 = CreateBlock(1, 2, "تم تأكيد الطلب", "كتلة 02: قبول وتأكيد الطلب #1258 من قِبل مخزن مستلزمات القهوة العالمية", ob1.CurrentHash, DateTime.Now.AddMinutes(-32));
            var ob3 = CreateBlock(1, 3, "تحديث المخزون", "كتلة 03: خصم (5 حبوب بن برازيلي + 10 أكواب ورقية) من الرصيد المتاح", ob2.CurrentHash, DateTime.Now.AddMinutes(-25));
            var ob4 = CreateBlock(1, 4, "تجهيز الطلب", "كتلة 04: تم تجهيز وتعبئة الأصناف وأصبح الطلب #1258 جاهزاً للاستلام/الشحن", ob3.CurrentHash, DateTime.Now.AddMinutes(-10));
            ord1.LedgerBlocks.AddRange(new[] { ob1, ob2, ob3, ob4 });

            // الطلب التجريبي الثاني رقم #1259
            var ord2 = new Order
            {
                OrderID = 2,
                OrderNumber = "1259",
                ClientUserID = 6,
                ClientName = "مطعم الشرق للمشويات والبرجر",
                ClientBusinessType = "مطعم",
                WarehouseID = 26,
                WarehouseName = "مخزن الذواقة للمجمدات وتوريدات المطاعم",
                OrderDate = DateTime.Now.AddHours(-3),
                TotalAmount = 2910.00m,
                OrderStatus = "تم التسليم",
                EstimatedArrivalMinutes = 0,
                DeliveryAddress = "القاهرة - المعادي - شارع 9 - مطعم الشرق للمشويات",
                IsChainValid = true,
                Items = new List<OrderItem>
                {
                    new OrderItem { OrderItemID = 3, OrderID = 2, ProductID = 376, ProductName = "بطاطس نصف مقلية مقرمشة للمطاعم (9mm)", SizeUnit = "كرتونة 10 كجم", Quantity = 1, UnitPrice = 580.00m, SubTotal = 580.00m },
                    new OrderItem { OrderItemID = 4, OrderID = 2, ProductID = 378, ProductName = "برجر بقري صافي متبل للمطاعم (150 جم)", SizeUnit = "كرتونة 5 كجم", Quantity = 1, UnitPrice = 1350.00m, SubTotal = 1350.00m },
                    new OrderItem { OrderItemID = 5, OrderID = 2, ProductID = 382, ProductName = "جبنة موزاريلا طبيعية مبشورة للبيتزا", SizeUnit = "كيس 5 كجم", Quantity = 1, UnitPrice = 980.00m, SubTotal = 980.00m }
                }
            };

            var b2_1 = CreateBlock(2, 1, "تم إنشاء الطلب", "كتلة 01: إنشاء الطلب #1259 بواسطة مطعم الشرق للمشويات بقيمة 2,910.00 ج.م", g0, DateTime.Now.AddMinutes(-180));
            var b2_2 = CreateBlock(2, 2, "تم تأكيد الطلب", "كتلة 02: تأكيد الطلب #1259 من مخزن الذواقة للمجمدات", b2_1.CurrentHash, DateTime.Now.AddMinutes(-165));
            var b2_3 = CreateBlock(2, 3, "تحديث المخزون", "كتلة 03: خصم أصناف الطلب #1259 من رصيد المخزن", b2_2.CurrentHash, DateTime.Now.AddMinutes(-150));
            var b2_4 = CreateBlock(2, 4, "تجهيز الطلب", "كتلة 04: تجهيز الطلب #1259 في سيارات التبريد", b2_3.CurrentHash, DateTime.Now.AddMinutes(-120));
            var b2_5 = CreateBlock(2, 5, "شحن الطلب", "كتلة 05: خروج الطلب #1259 في الطريق إلى العميل", b2_4.CurrentHash, DateTime.Now.AddMinutes(-90));
            var b2_6 = CreateBlock(2, 6, "تسليم الطلب", "كتلة 06: تم تسليم الطلب #1259 بنجاح وتوقيع الاستلام الإلكتروني", b2_5.CurrentHash, DateTime.Now.AddMinutes(-45));
            ord2.LedgerBlocks.AddRange(new[] { b2_1, b2_2, b2_3, b2_4, b2_5, b2_6 });

            s.Orders.Add(ord1);
            s.Orders.Add(ord2);

            return s;
        }
    }
}
