use rand::Rng;
use serde::Serialize;

#[derive(Serialize)]
struct ResultData {
    a: i32,
    b: i32,
    sum: i32,
}

fn main() {
    let mut rng = rand::thread_rng();
    let a: i32 = rng.gen_range(0..100);
    let b: i32 = rng.gen_range(0..100);
    let result = ResultData { a, b, sum: a + b };

    let json = serde_json::to_string(&result).unwrap();
    println!("{}", json);
}
