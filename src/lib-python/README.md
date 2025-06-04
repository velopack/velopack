
# Prerequisites
- Install UV
  https://docs.astral.sh/uv/getting-started/installation/
    - If you have rust already, you can build from source with:
      ```
      cargo install --git https://github.com/astral-sh/uv uv
      ```
    - Also can use winget for a pre-compiled version:
      ```
      winget install --id=astral-sh.uv -e
      ```

# Running the tests
 - `uv sync`
 - `cd test`
 - `uv run python run_test.py`

 # Building / Publishing
 todo