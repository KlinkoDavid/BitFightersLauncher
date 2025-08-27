<?php
// MOCK API - BitFighters Launcher
// Ez egy egyszerû mock API, amely nem igényel adatbázist
// Csak tesztelési célokra!

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: POST, GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');

// OPTIONS kérés kezelése (preflight)
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    exit(0);
}

// HTTP metódus ellenõrzése
$method = $_SERVER['REQUEST_METHOD'];

try {
    // Kérés típusa szerint adatok kinyerése
    if ($method === 'GET') {
        $data = $_GET;
    } else {
        $json_input = file_get_contents("php://input");
        if (!empty($json_input)) {
            $data = json_decode($json_input, true);
            if (json_last_error() !== JSON_ERROR_NONE) {
                throw new Exception("JSON parsing error: " . json_last_error_msg());
            }
        } else {
            $data = $_POST;
        }
    }

    // Action paraméter ellenõrzése
    if (!isset($data['action']) || empty($data['action'])) {
        http_response_code(400);
        echo json_encode([
            "success" => false, 
            "message" => "Hiányzó 'action' paraméter",
            "available_actions" => [
                "login", "get_user_score", "get_user_score_by_id", 
                "update_user_score", "get_users", "get_leaderboard", "get_news", "test"
            ]
        ], JSON_UNESCAPED_UNICODE);
        exit;
    }

    // MOCK adatok
    $mock_users = [
        'testuser' => [
            'id' => 1,
            'username' => 'testuser',
            'password' => 'testpass',
            'highest_score' => 1500,
            'created_at' => '2024-01-15 10:30:00'
        ],
        'player1' => [
            'id' => 2,
            'username' => 'player1',
            'password' => 'pass123',
            'highest_score' => 2300,
            'created_at' => '2024-01-20 14:15:00'
        ],
        'gamer2024' => [
            'id' => 3,
            'username' => 'gamer2024',
            'password' => 'gaming',
            'highest_score' => 800,
            'created_at' => '2024-02-01 09:45:00'
        ]
    ];

    // Action switch
    switch (strtolower($data['action'])) {
        
        case 'test':
            echo json_encode([
                "success" => true,
                "message" => "MOCK API mûködik!",
                "database" => "mock_database",
                "user_count" => count($mock_users),
                "timestamp" => date('Y-m-d H:i:s'),
                "note" => "Ez egy mock API tesztelési célokra"
            ], JSON_UNESCAPED_UNICODE);
            break;
        
        case 'login':
            if (empty($data['username']) || empty($data['password'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hiányzó username vagy password paraméter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            $username = $data['username'];
            $password = $data['password'];
            
            if (isset($mock_users[$username]) && $mock_users[$username]['password'] === $password) {
                $user = $mock_users[$username];
                echo json_encode([
                    "success" => true,
                    "message" => "Sikeres bejelentkezés",
                    "user" => [
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score'],
                        "created_at" => $user['created_at']
                    ]
                ], JSON_UNESCAPED_UNICODE);
            } else {
                echo json_encode([
                    "success" => false, 
                    "message" => "Hibás felhasználónév vagy jelszó",
                    "hint" => "Próbáld: testuser/testpass vagy player1/pass123"
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_user_score':
            if (empty($data['username'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hiányzó username paraméter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            $username = $data['username'];
            if (isset($mock_users[$username])) {
                $user = $mock_users[$username];
                echo json_encode([
                    "success" => true,
                    "message" => "Pontszám sikeresen lekérdezve",
                    "user" => [
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score']
                    ]
                ], JSON_UNESCAPED_UNICODE);
            } else {
                echo json_encode([
                    "success" => false, 
                    "message" => "Felhasználó nem található",
                    "username" => $username
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        case 'get_news':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 20;
            if ($limit > 100) $limit = 100;
            
            $news = [
                [
                    "id" => 1,
                    "title" => "?? Üdvözöljük a BitFighters világában!",
                    "content" => "A launcher sikeresen betöltött és az API tökéletesen mûködik! Ez egy teszt hír, amely bizonyítja, hogy a kommunikáció a kliens és a szerver között zavartalan. Készülj fel az epikus csatákra!",
                    "created_at" => date('Y-m-d H:i:s')
                ],
                [
                    "id" => 2,
                    "title" => "?? API Teszt Sikeres",
                    "content" => "Minden API endpoint mûködik! A bejelentkezés, pontszám lekérdezés, és hírek betöltése mind flawlessly mûködik. Most már csak a játékot kell elindítani!",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-2 hours'))
                ],
                [
                    "id" => 3,
                    "title" => "?? Új Funkciók",
                    "content" => "A launcher most már támogatja a felhasználói profilokat, pontszám követést, és automatikus frissítéseket. Élvezd a gaming élményt!",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-1 day'))
                ],
                [
                    "id" => 4,
                    "title" => "?? Leaderboard Aktiválva",
                    "content" => "Most már megtekintheted a ranglistát és összehasonlíthatod pontszámaidat más játékosokkal. Ki lesz a BitFighters bajnoka?",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-2 days'))
                ],
                [
                    "id" => 5,
                    "title" => "?? Rendszer Optimalizáció",
                    "content" => "A launcher teljesítményét optimalizáltuk a gyorsabb betöltés és zökkenõmentes felhasználói élmény érdekében.",
                    "created_at" => date('Y-m-d H:i:s', strtotime('-3 days'))
                ]
            ];
            
            $news = array_slice($news, 0, $limit);
            echo json_encode($news, JSON_UNESCAPED_UNICODE);
            break;
            
        case 'get_users':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 100;
            if ($limit > 500) $limit = 500;
            
            $users = [];
            foreach ($mock_users as $user) {
                $users[] = [
                    "id" => $user['id'],
                    "username" => $user['username'],
                    "highest_score" => $user['highest_score']
                ];
            }
            
            // Pontszám szerint rendezés
            usort($users, function($a, $b) {
                return $b['highest_score'] - $a['highest_score'];
            });
            
            $users = array_slice($users, 0, $limit);
            
            echo json_encode([
                "success" => true,
                "message" => "Felhasználók sikeresen lekérdezve",
                "users" => $users,
                "count" => count($users)
            ], JSON_UNESCAPED_UNICODE);
            break;
            
        case 'get_leaderboard':
            $limit = isset($data['limit']) ? (int)$data['limit'] : 10;
            if ($limit > 100) $limit = 100;
            
            $leaderboard = [];
            $rank = 1;
            foreach ($mock_users as $user) {
                if ($user['highest_score'] > 0) {
                    $leaderboard[] = [
                        "rank" => $rank,
                        "id" => $user['id'],
                        "username" => $user['username'],
                        "highest_score" => $user['highest_score']
                    ];
                    $rank++;
                }
            }
            
            // Pontszám szerint rendezés
            usort($leaderboard, function($a, $b) {
                return $b['highest_score'] - $a['highest_score'];
            });
            
            // Rangok újraszámolása
            for ($i = 0; $i < count($leaderboard); $i++) {
                $leaderboard[$i]['rank'] = $i + 1;
            }
            
            $leaderboard = array_slice($leaderboard, 0, $limit);
            
            echo json_encode([
                "success" => true,
                "message" => "Ranglista sikeresen lekérdezve",
                "leaderboard" => $leaderboard,
                "count" => count($leaderboard)
            ], JSON_UNESCAPED_UNICODE);
            break;
            
        case 'update_user_score':
            if (empty($data['user_id']) || !isset($data['new_score'])) {
                http_response_code(400);
                echo json_encode([
                    "success" => false, 
                    "message" => "Hiányzó user_id vagy new_score paraméter"
                ], JSON_UNESCAPED_UNICODE);
                break;
            }
            
            $user_id = (int)$data['user_id'];
            $new_score = (int)$data['new_score'];
            
            // User keresése ID alapján
            $found_user = null;
            foreach ($mock_users as $username => $user) {
                if ($user['id'] === $user_id) {
                    $found_user = $user;
                    break;
                }
            }
            
            if ($found_user) {
                $current_score = $found_user['highest_score'];
                
                if ($new_score > $current_score) {
                    // MOCK-ban nem mentjük el, csak visszaadjuk a választ
                    echo json_encode([
                        "success" => true,
                        "message" => "Pontszám sikeresen frissítve (MOCK)",
                        "user" => [
                            "id" => $user_id,
                            "username" => $found_user['username'],
                            "old_score" => $current_score,
                            "new_score" => $new_score
                        ]
                    ], JSON_UNESCAPED_UNICODE);
                } else {
                    echo json_encode([
                        "success" => false,
                        "message" => "Az új pontszám nem nagyobb a jelenleginél",
                        "current_score" => $current_score,
                        "submitted_score" => $new_score
                    ], JSON_UNESCAPED_UNICODE);
                }
            } else {
                echo json_encode([
                    "success" => false, 
                    "message" => "Felhasználó nem található",
                    "user_id" => $user_id
                ], JSON_UNESCAPED_UNICODE);
            }
            break;
            
        default:
            http_response_code(400);
            echo json_encode([
                "success" => false, 
                "message" => "Ismeretlen action: " . $data['action'],
                "available_actions" => [
                    "login", "get_user_score", "get_user_score_by_id", 
                    "update_user_score", "get_users", "get_leaderboard", "get_news", "test"
                ]
            ], JSON_UNESCAPED_UNICODE);
            break;
    }

} catch (Exception $e) {
    http_response_code(500);
    echo json_encode([
        "success" => false,
        "message" => "Hiba: " . $e->getMessage()
    ], JSON_UNESCAPED_UNICODE);
}
?>